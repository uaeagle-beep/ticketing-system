using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TicketTracker.Tests.Infrastructure;

/// <summary>
/// Base class for WebApplicationFactory-backed HTTP integration tests. Each TEST gets its own
/// factory instance, and therefore its own fresh in-memory SQLite database, capturing
/// FakeEmailSender and TestClock — so tests are fully isolated and order-independent (ADR-0002:
/// "tests do not share state"). The factory is in-process and EnsureCreated() is sub-second, so
/// per-test construction is cheap. Provides high-level flow helpers (signup → capture link →
/// verify → login) and thin typed HTTP wrappers so the tests read as the specification, not as
/// plumbing. All calls go over real HTTP through HttpClient — no service is invoked directly.
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected readonly CustomWebApplicationFactory Factory;
    protected readonly HttpClient Client;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The configured default signup team (F-10). Blank by default ⇒ auto-provisioning OFF (the pre-Wave-1
    /// baseline: no teams until one is created). Test classes that exercise the default-team auto-create /
    /// auto-join branch override this to return "Demo Team".
    /// </summary>
    protected virtual string DefaultSignupTeamName => string.Empty;

    protected IntegrationTestBase()
    {
        Factory = new CustomWebApplicationFactory { DefaultSignupTeamName = DefaultSignupTeamName };
        Factory.Clock.SetUtcNow(new DateTime(2026, 06, 30, 12, 00, 00, DateTimeKind.Utc));
        Client = Factory.CreateClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }

    // ---------- Auth flow helpers ----------

    protected const string DefaultPassword = "correct horse battery";

    /// <summary>
    /// Signup → verify → login. Returns (token, userId, email). After the authorization model
    /// (ADR-0007) a freshly self-registered user is a TEAM-LESS MEMBER, which would lack access to
    /// the team-scoped surface most existing tests exercise. Per design §6.3 the default test
    /// principal is therefore PROMOTED TO ADMIN here, so business-rule tests keep full access; the
    /// dedicated authz tests use <see cref="RegisterMemberInTeamAsync"/> / <see cref="RegisterMemberAsync"/>.
    /// </summary>
    protected Task<(string Token, Guid UserId, string Email)> RegisterVerifiedUserAsync(
        string? email = null, string password = DefaultPassword)
        => RegisterAdminAsync(email, password);

    /// <summary>Signup → verify → login → promote to admin. Returns (token, userId, email).</summary>
    protected async Task<(string Token, Guid UserId, string Email)> RegisterAdminAsync(
        string? email = null, string password = DefaultPassword)
    {
        var (token, userId, resolvedEmail) = await SignupVerifyLoginAsync(email, password);
        await Factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.IsAdmin = true;
            await db.SaveChangesAsync();
        });
        return (token, userId, resolvedEmail);
    }

    /// <summary>Signup → verify → login as a plain (team-less) MEMBER. Returns (token, userId, email).</summary>
    protected Task<(string Token, Guid UserId, string Email)> RegisterMemberAsync(
        string? email = null, string password = DefaultPassword)
        => SignupVerifyLoginAsync(email, password);

    /// <summary>
    /// Signup → verify → login as a MEMBER, then grant membership in the given team(s) directly in the
    /// DB (the authz tests' building block). Returns (token, userId, email).
    /// </summary>
    protected async Task<(string Token, Guid UserId, string Email)> RegisterMemberInTeamAsync(
        params Guid[] teamIds)
    {
        var (token, userId, email) = await SignupVerifyLoginAsync(null, DefaultPassword);
        await AddMembershipAsync(userId, teamIds);
        return (token, userId, email);
    }

    /// <summary>Grant the user membership in the given team(s) directly in persistence.</summary>
    protected Task AddMembershipAsync(Guid userId, params Guid[] teamIds)
        => Factory.WithDbAsync(async db =>
        {
            var now = Factory.Clock.UtcNow;
            foreach (var teamId in teamIds)
            {
                var already = await db.UserTeams.AnyAsync(m => m.UserId == userId && m.TeamId == teamId);
                if (!already)
                    db.UserTeams.Add(new TicketTracker.Domain.Entities.UserTeam
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        TeamId = teamId,
                        CreatedAt = now
                    });
            }
            await db.SaveChangesAsync();
        });

    /// <summary>Block a user and purge their sessions directly in persistence (mirrors the admin op).</summary>
    protected Task BlockUserAsync(Guid userId)
        => Factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.IsBlocked = true;
            var sessions = await db.Sessions.Where(s => s.UserId == userId).ToListAsync();
            db.Sessions.RemoveRange(sessions);
            await db.SaveChangesAsync();
        });

    private async Task<(string Token, Guid UserId, string Email)> SignupVerifyLoginAsync(
        string? email, string password)
    {
        email ??= $"user-{Guid.NewGuid():N}@dataart.com";

        var signup = await Client.PostAsJsonAsync("/api/auth/signup", new { email, password });
        signup.StatusCode.Should().Be(HttpStatusCode.Created,
            "signup of a fresh email returns 201 (API_CONTRACT §3.1)");

        var captured = Factory.Email.LastFor(email.Trim());
        captured.Should().NotBeNull("a verification email must have been captured for the new account");
        var token = Fakes.FakeEmailSender.ExtractToken(captured!.Link);

        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new { token });
        verify.StatusCode.Should().Be(HttpStatusCode.OK, "the fresh token verifies the account");

        var login = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK, "a verified account can log in");
        var body = await ReadAsync<LoginDto>(login);
        return (body.Token, body.User.Id, body.User.Email);
    }

    /// <summary>
    /// Drive the email outbox drain deterministically (Wave 2, ADR-0014 / §7.5): resolve the
    /// <c>NotificationEmailDispatcher</c> from a scope and call <c>DrainOnceAsync(Factory.Clock.UtcNow, ...)</c>.
    /// Returns the number of recipients emailed. Tests advance <c>Factory.Clock</c> past the debounce window
    /// before draining to observe coalescing; the hosted worker is removed so no timer competes.
    /// </summary>
    protected async Task<int> DrainNotificationEmailsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var dispatcher = scope.ServiceProvider
            .GetRequiredService<TicketTracker.Application.Services.NotificationEmailDispatcher>();
        return await dispatcher.DrainOnceAsync(Factory.Clock.UtcNow, CancellationToken.None);
    }

    /// <summary>
    /// Drive the webhook delivery outbox drain deterministically (Wave 3, ADR-0021, §8.4): resolve the
    /// <c>WebhookDeliveryDispatcher</c> from a scope and call <c>DrainOnceAsync(Factory.Clock.UtcNow, ...)</c>
    /// with the fake <c>IWebhookSender</c>. Returns the number of deliveries attempted. The hosted worker is
    /// removed so no timer competes; tests advance <c>Factory.Clock</c> to observe backoff/retry.
    /// </summary>
    protected async Task<int> DrainWebhookDeliveriesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var dispatcher = scope.ServiceProvider
            .GetRequiredService<TicketTracker.Application.Services.WebhookDeliveryDispatcher>();
        return await dispatcher.DrainOnceAsync(Factory.Clock.UtcNow, CancellationToken.None);
    }

    /// <summary>An HttpClient with the given bearer token attached (no auth attached on the base client).</summary>
    protected HttpClient Authed(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ---------- Typed HTTP wrappers ----------

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var value = await JsonSerializer.DeserializeAsync<T>(stream, Json);
        value.Should().NotBeNull("response body for {0} should deserialize to {1}",
            response.RequestMessage?.RequestUri, typeof(T).Name);
        return value!;
    }

    protected static async Task<ErrorBodyDto> ReadErrorAsync(HttpResponseMessage response)
    {
        var envelope = await ReadAsync<ErrorEnvelopeDto>(response);
        envelope.Error.Should().NotBeNull("non-2xx responses use the uniform error envelope (API_CONTRACT §2)");
        return envelope.Error;
    }
}
