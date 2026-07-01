using System.Text.Json;
using System.Text.Json.Serialization;
using TicketTracker.Api.Auth;
using TicketTracker.Api.HostedServices;
using TicketTracker.Api.Middleware;
using TicketTracker.Application;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Options;
using TicketTracker.Infrastructure;
using TicketTracker.Infrastructure.Email;
using TicketTracker.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ---------- JSON: camelCase, ISO-8601 UTC with trailing Z, ignore nulls is NOT desired
// (epicId/epicTitle must serialize as null per API_CONTRACT). ----------
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
};
// Singleton instance shared with the custom middleware (which serialize the error envelope).
builder.Services.AddSingleton(jsonOptions);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });

// ---------- Options bound from environment (ARCHITECTURE §8). ----------
builder.Services.Configure<AuthOptions>(o =>
{
    var config = builder.Configuration;
    o.TokenTtlHours = config.GetValue("TOKEN_TTL_HOURS", 24);
    o.PasswordResetTtlHours = config.GetValue("PASSWORD_RESET_TTL_HOURS", 1);
    o.SessionTtlHours = config.GetValue("SESSION_TTL_HOURS", 72);
    o.FrontendUrl = config.GetValue("FRONTEND_URL", "http://localhost:8080") ?? "http://localhost:8080";
    o.DefaultSignupTeamName = config.GetValue("DEFAULT_SIGNUP_TEAM_NAME", "Demo Team") ?? "Demo Team";
});

builder.Services.Configure<NotificationOptions>(o =>
{
    var config = builder.Configuration;
    o.WorkerPollSeconds = config.GetValue("NOTIFICATION_WORKER_POLL_SECONDS", 15);
    o.EmailDebounceSeconds = config.GetValue("NOTIFICATION_EMAIL_DEBOUNCE_SECONDS", 60);
    o.EmailEnabled = config.GetValue("NOTIFICATIONS_EMAIL_ENABLED", true);
    o.FrontendUrl = config.GetValue("FRONTEND_URL", "http://localhost:8080") ?? "http://localhost:8080";
});

builder.Services.Configure<AttachmentOptions>(o =>
{
    var config = builder.Configuration;
    o.Root = config.GetValue("ATTACHMENTS_ROOT", "/var/lib/tickettracker/attachments")
             ?? "/var/lib/tickettracker/attachments";
    o.MaxBytes = config.GetValue("ATTACHMENTS_MAX_BYTES", 10L * 1024 * 1024);
});

// Webhook delivery outbox tuning (Wave 3, ADR-0021, §8.3). WEBHOOKS_ALLOW_INSECURE stays false in prod.
// FAIL-FAST: this flag disables the entire SSRF defense (http:// + private/loopback/metadata block skipped),
// so a production deploy must never enable it — a silent misconfig would open SSRF to the compose network /
// cloud metadata. Refuse to boot in Production if it is set true (mirrors the WEBHOOK_SIGNING_KEY guard).
var webhooksAllowInsecure = builder.Configuration.GetValue("WEBHOOKS_ALLOW_INSECURE", false);
if (webhooksAllowInsecure && builder.Environment.IsProduction())
    throw new InvalidOperationException(
        "WEBHOOKS_ALLOW_INSECURE must not be true in Production: it disables the webhook SSRF defense " +
        "(https-only + private/loopback/link-local/metadata block). Unset it or set it to false (see ADR-0021 §7.4).");
builder.Services.Configure<WebhookOptions>(o =>
{
    var config = builder.Configuration;
    o.Enabled = config.GetValue("WEBHOOKS_ENABLED", true);
    o.WorkerPollSeconds = config.GetValue("WEBHOOK_WORKER_POLL_SECONDS", 10);
    o.MaxAttempts = config.GetValue("WEBHOOK_MAX_ATTEMPTS", 5);
    o.TimeoutSeconds = config.GetValue("WEBHOOK_TIMEOUT_SECONDS", 10);
    o.AllowInsecure = webhooksAllowInsecure;
});

builder.Services.Configure<SmtpOptions>(o =>
{
    var config = builder.Configuration;
    o.Host = config.GetValue("SMTP_HOST", "relay1.dataart.com") ?? "relay1.dataart.com";
    o.Port = config.GetValue("SMTP_PORT", 587);
    o.Username = config.GetValue("SMTP_USERNAME", string.Empty) ?? string.Empty;
    o.Password = config.GetValue("SMTP_PASSWORD", string.Empty) ?? string.Empty;
    o.UseStartTls = config.GetValue("SMTP_USE_STARTTLS", true);
    o.From = config.GetValue("EMAIL_FROM", "no-reply@ticketing.local") ?? "no-reply@ticketing.local";
});

// ---------- Persistence (swappable for tests — ADR-0002) ----------
builder.Services.AddAppPersistence(builder.Configuration);

// ---------- Infrastructure adapters (hasher, token gen, clock, email) ----------
builder.Services.AddInfrastructureAdapters(builder.Configuration, builder.Environment.IsProduction());

// ---------- Application services ----------
builder.Services.AddApplicationServices();

// Strong-password generator for admin-created accounts / resets (ADR-0007). Registered here (not in
// the Infrastructure adapters extension, which is out of scope for this change) — it is a stateless
// CSPRNG adapter with no configuration.
builder.Services.AddSingleton<TicketTracker.Application.Abstractions.IPasswordGenerator,
    TicketTracker.Infrastructure.Security.CryptoPasswordGenerator>();

// Attachment blob storage (Wave 3, ADR-0018). Production binds the local-filesystem impl; the test
// factory replaces IAttachmentStorage with an in-memory impl so integration tests never touch disk.
builder.Services.AddSingleton<TicketTracker.Application.Abstractions.IAttachmentStorage,
    TicketTracker.Infrastructure.Storage.LocalFileAttachmentStorage>();

// ---------- Webhooks (Wave 3, ADR-0021) ----------
// AES-GCM secret protector keyed by WEBHOOK_SIGNING_KEY. Like AUTH_TOKEN_SECRET: FAIL-FAST in Production on
// a missing/empty key; other environments fall back to a built-in dev key so tests run without configuration.
const string developmentWebhookSigningKey = "ticket-tracker-dev-webhook-signing-key-not-for-production";
var webhookSigningKey = builder.Configuration["WEBHOOK_SIGNING_KEY"];
if (string.IsNullOrWhiteSpace(webhookSigningKey))
{
    if (builder.Environment.IsProduction())
        throw new InvalidOperationException(
            "WEBHOOK_SIGNING_KEY is required in Production but was not configured. " +
            "Set a strong random value (see .env.example / ADR-0021).");
    webhookSigningKey = developmentWebhookSigningKey;
}
builder.Services.AddSingleton<TicketTracker.Application.Abstractions.ISecretProtector>(
    _ => new TicketTracker.Infrastructure.Security.AesGcmSecretProtector(webhookSigningKey));

// SSRF policy (subscribe-time scheme check + send-time private-IP block). Reads WebhookOptions.AllowInsecure.
builder.Services.AddScoped<TicketTracker.Application.Abstractions.IWebhookUrlValidator,
    TicketTracker.Infrastructure.Webhooks.WebhookUrlValidator>();

// Outbound HTTP for webhook delivery via IHttpClientFactory. AllowAutoRedirect=false so a subscriber can
// never 3xx-bounce the request to an internal target (SSRF, §7.4). The per-attempt timeout is applied in
// the sender via a linked CTS. The test factory replaces IWebhookSender with a fake (no real sockets).
builder.Services.AddHttpClient(TicketTracker.Api.Webhooks.HttpWebhookSender.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<TicketTracker.Application.Abstractions.IWebhookSender,
    TicketTracker.Api.Webhooks.HttpWebhookSender>();

// ---------- Real-time board via SignalR (Wave 3, ADR-0019) ----------
// The hub at /hubs/board is a near-empty transport shell (connect-auth + group join/leave); all push
// correctness lives in the RealtimeNotifier event-backbone handler over the IRealtimeNotifier seam.
builder.Services.AddSignalR();
// Bind the production notifier AFTER AddApplicationServices() (which TryAdds a NullRealtimeNotifier default),
// so the last registration wins when a single IRealtimeNotifier is resolved. Scoped to match the seam's
// lifetime alongside the scoped handlers; IHubContext<BoardHub> is itself a singleton the wrapper captures.
builder.Services.AddScoped<IRealtimeNotifier,
    TicketTracker.Api.Realtime.SignalRRealtimeNotifier>();

// ---------- Auth current-user (scoped); exposed via ICurrentUser ----------
builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserAccessor>());

// ---------- DB readiness + migration hosted service (ADR-0003) ----------
builder.Services.AddSingleton<DatabaseReadinessState>();
builder.Services.AddHostedService<DatabaseInitializer>();

// ---------- Notification email outbox worker (ADR-0014). Thin timer over DrainOnceAsync; the test
// factory REMOVES this hosted service so no timer fires during tests (§7.5). ----------
builder.Services.AddHostedService<NotificationEmailWorker>();

// ---------- Webhook delivery outbox worker (Wave 3, ADR-0021, §8). Thin timer over DrainOnceAsync; the
// test factory REMOVES this hosted service too so no timer fires during tests (R-A13). ----------
builder.Services.AddHostedService<WebhookDeliveryWorker>();

// ---------- CORS (SCT-001 / AUTH-006) ----------
// Development: permissive policy for direct API testing.
// Production: single-origin via nginx (no CORS) unless FRONTEND_URL is set, in which case
// allow EXACTLY that origin. AllowAnyOrigin must never be active in Production.
var corsFrontendOrigin = builder.Configuration.GetValue<string>("FRONTEND_URL");
var useProductionCors = builder.Environment.IsProduction() && !string.IsNullOrWhiteSpace(corsFrontendOrigin);
var enableCors = builder.Environment.IsDevelopment() || useProductionCors;

if (enableCors)
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (builder.Environment.IsDevelopment())
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            }
            else
            {
                // Production with an explicit FRONTEND_URL: lock to exactly that origin.
                policy.WithOrigins(corsFrontendOrigin!.TrimEnd('/'))
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            }
        });
    });
}

var app = builder.Build();

// ---------- Middleware pipeline ----------
// Exception mapper first so every downstream error becomes the uniform envelope.
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (enableCors)
    app.UseCors();
// Bearer auth gate (ADR-0001) — guards /api/* except the public allowlist.
app.UseMiddleware<BearerAuthMiddleware>();

app.MapControllers();

// Real-time hub (Wave 3, ADR-0019). BearerAuthMiddleware only guards /api/*, so /hubs/board passes through
// untouched — the hub does its OWN connect-time auth (?access_token= → AuthService.ResolveSessionUserAsync)
// and aborts unauthenticated/blocked connections. nginx proxies this path with the WebSocket Upgrade headers
// and access_log off (so the token in the query string is never logged, §9.2).
app.MapHub<TicketTracker.Api.Realtime.BoardHub>(TicketTracker.Api.Realtime.BoardHub.Path);

app.Run();

/// <summary>
/// Exposed as public partial so the test project's <c>WebApplicationFactory&lt;Program&gt;</c>
/// can boot the app and swap the DbContext provider (ARCHITECTURE §10, ADR-0002).
/// </summary>
public partial class Program { }
