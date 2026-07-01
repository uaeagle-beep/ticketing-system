using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TicketTracker.Application.Abstractions;
using TicketTracker.Infrastructure.Persistence;
using TicketTracker.Tests.Fakes;

namespace TicketTracker.Tests.Infrastructure;

/// <summary>
/// Boots the real ASP.NET Core app via <see cref="WebApplicationFactory{TEntryPoint}"/> and
/// substitutes the persistence + side-effecting adapters so integration tests run real HTTP
/// without Docker and without PostgreSQL (ARCHITECTURE §10, ADR-0002, ADR-0004).
///
/// Per the backend developer's critical instructions:
///  - sets RUN_MIGRATIONS_ON_STARTUP=false so the migrate-on-startup hosted service is a no-op
///    (and additionally removes that hosted service) — otherwise MigrateAsync would run
///    Npgsql-specific SQL against SQLite and stall startup;
///  - removes the Npgsql AppDbContext / DbContextOptions registrations and re-adds the context
///    over a single, kept-open in-memory SQLite connection (closing the connection drops the DB);
///  - enables PRAGMA foreign_keys=ON on that connection so FK RESTRICT/CASCADE behaviors fire;
///  - builds the schema with EnsureCreated() (NOT Migrate()), which is provider-agnostic;
///  - swaps IEmailSender for a capturing <see cref="FakeEmailSender"/> and IClock for a
///    controllable <see cref="TestClock"/> (both singletons so state survives across requests).
///
/// Each test class gets its own factory instance and therefore its own fresh database.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    /// <summary>The fake email sender singleton — inspect captured verification links here.</summary>
    public FakeEmailSender Email { get; } = new();

    /// <summary>The controllable clock singleton — advance it to test TTL/modified_at semantics.</summary>
    public TestClock Clock { get; } = new();

    /// <summary>
    /// The in-memory attachment blob store (Wave 3, ADR-0018) — inspect stored keys/bytes here. Bound
    /// so integration tests over SQLite never touch the real filesystem volume (§11).
    /// </summary>
    public InMemoryAttachmentStorage AttachmentStorage { get; } = new();

    /// <summary>
    /// The fake webhook sender singleton (Wave 3, ADR-0021, §8.4) — script the response status + inspect the
    /// signed requests here. Bound so the delivery drain runs deterministically over SQLite with no real HTTP.
    /// </summary>
    public FakeWebhookSender WebhookSender { get; } = new();

    /// <summary>
    /// The recording real-time notifier singleton (Wave 3, ADR-0019, §11 B) — assert which thin push signals
    /// (board/ticket changed, user bell ping) the event backbone emitted. Bound in place of the production
    /// SignalRRealtimeNotifier so integration tests never need a live WebSocket / IHubContext (the hub mapping
    /// still exists in the app but is not exercised over the HTTP integration surface).
    /// </summary>
    public RecordingRealtimeNotifier Realtime { get; } = new();

    /// <summary>
    /// The configured default signup team name (F-10). Defaults to BLANK so auto-provisioning is OFF for
    /// the general test population (matching the pre-Wave-1 baseline — a fresh DB has no teams until one
    /// is created). The default-team feature tests set this to "Demo Team" to exercise the auto-create /
    /// auto-join branch. Must be assigned before the first CreateClient() (which builds the host).
    /// </summary>
    public string DefaultSignupTeamName { get; init; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Disable the migrate-on-startup path (DatabaseInitializer honors this flag, see ADR-0003).
        builder.UseSetting("RUN_MIGRATIONS_ON_STARTUP", "false");
        // Keep the verification-link base URL deterministic for token-extraction assertions.
        builder.UseSetting("FRONTEND_URL", "http://localhost:8080");
        // Auto-provisioning (F-10) is opt-in for tests: blank ⇒ verify grants no default team.
        builder.UseSetting("DEFAULT_SIGNUP_TEAM_NAME", DefaultSignupTeamName);
        // Webhooks (Wave 3): allow http/localhost + skip the private-IP SSRF block so tests can target
        // local endpoints and drive the drain deterministically with the fake sender (§8.4). The signing
        // key is defaulted in non-Production so tests need no WEBHOOK_SIGNING_KEY (fail-fast is Production-only).
        builder.UseSetting("WEBHOOKS_ALLOW_INSECURE", "true");

        builder.ConfigureServices(services =>
        {
            // ---- Remove the Npgsql AppDbContext registration (ADR-0002) ----
            RemoveAll(services, typeof(DbContextOptions<AppDbContext>));
            RemoveAll(services, typeof(DbContextOptions));
            RemoveAll(services, typeof(AppDbContext));
            RemoveAll(services, typeof(IAppDbContext));
            // EF Core 10 also registers internal infra keyed to the provider; drop pooling helpers too.
            RemoveAll(services, typeof(IDbContextOptionsConfiguration<AppDbContext>));

            // ---- Remove the hosted background services so they can never fire during a test ----
            //  - DatabaseInitializer: would run Npgsql-specific migration SQL against SQLite (ADR-0003);
            //    belt-and-suspenders alongside RUN_MIGRATIONS_ON_STARTUP=false.
            //  - NotificationEmailWorker: its PeriodicTimer would drain the outbox non-deterministically
            //    (ADR-0014, R-8). Tests drive NotificationEmailDispatcher.DrainOnceAsync directly with the
            //    fake clock + fake sender, so the timer MUST NOT run.
            //  - WebhookDeliveryWorker: its PeriodicTimer would drain the webhook outbox non-deterministically
            //    (Wave 3, ADR-0021, R-A13). Tests drive WebhookDeliveryDispatcher.DrainOnceAsync directly with
            //    the fake clock + fake sender, so the timer MUST NOT run.
            var hostedToRemove = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                            && d.ImplementationType is not null
                            && (d.ImplementationType.Name == "DatabaseInitializer"
                                || d.ImplementationType.Name == "NotificationEmailWorker"
                                || d.ImplementationType.Name == "WebhookDeliveryWorker"))
                .ToList();
            foreach (var d in hostedToRemove)
                services.Remove(d);

            // ---- Open a single in-memory SQLite connection kept alive for the test lifetime ----
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            // FK enforcement is OFF by default in SQLite — turn it on so RESTRICT/CASCADE fire.
            using (var pragma = _connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys=ON;";
                pragma.ExecuteNonQuery();
            }

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection!));
            services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

            // ---- Swap side-effecting adapters for test doubles ----
            RemoveAll(services, typeof(IEmailSender));
            services.AddSingleton<IEmailSender>(Email);

            RemoveAll(services, typeof(IClock));
            services.AddSingleton<IClock>(Clock);

            // Attachment storage: replace the local-filesystem impl with the in-memory test store so
            // uploads/downloads/deletes never touch disk (Wave 3, ADR-0018, §11).
            RemoveAll(services, typeof(IAttachmentStorage));
            services.AddSingleton<IAttachmentStorage>(AttachmentStorage);

            // Webhook sender: replace the real HTTP sender with the fake so the delivery drain returns
            // scripted statuses and captures the signed requests (Wave 3, ADR-0021, §8.4). No real sockets.
            RemoveAll(services, typeof(IWebhookSender));
            services.AddSingleton<IWebhookSender>(WebhookSender);

            // Real-time notifier: replace the production SignalRRealtimeNotifier (which needs IHubContext /
            // a live socket) with a recording fake so the RealtimeNotifier handler + NotificationFanout bell
            // ping run and are asserted with no WebSocket (Wave 3, ADR-0019, §11 B). The hub mapping still
            // exists in the app but is never exercised by the HTTP integration tests.
            RemoveAll(services, typeof(IRealtimeNotifier));
            services.AddSingleton<IRealtimeNotifier>(Realtime);

            // ---- Build the schema from the model (provider-agnostic), once ----
            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    /// <summary>
    /// Run an action against a scoped <see cref="AppDbContext"/> over the SAME in-memory database the
    /// app uses. Lets authz tests seed/mutate persistence directly (e.g. promote a user to admin,
    /// add a membership, block a user) without an HTTP bootstrap path that does not yet exist.
    /// </summary>
    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
    }

    private static void RemoveAll(IServiceCollection services, Type serviceType)
    {
        var toRemove = services.Where(d => d.ServiceType == serviceType).ToList();
        foreach (var d in toRemove)
            services.Remove(d);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
