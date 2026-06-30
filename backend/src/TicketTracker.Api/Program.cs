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
    o.SessionTtlHours = config.GetValue("SESSION_TTL_HOURS", 72);
    o.FrontendUrl = config.GetValue("FRONTEND_URL", "http://localhost:8080") ?? "http://localhost:8080";
    o.DefaultSignupTeamName = config.GetValue("DEFAULT_SIGNUP_TEAM_NAME", "Demo Team") ?? "Demo Team";
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

// ---------- Auth current-user (scoped); exposed via ICurrentUser ----------
builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserAccessor>());

// ---------- DB readiness + migration hosted service (ADR-0003) ----------
builder.Services.AddSingleton<DatabaseReadinessState>();
builder.Services.AddHostedService<DatabaseInitializer>();

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

app.Run();

/// <summary>
/// Exposed as public partial so the test project's <c>WebApplicationFactory&lt;Program&gt;</c>
/// can boot the app and swap the DbContext provider (ARCHITECTURE §10, ADR-0002).
/// </summary>
public partial class Program { }
