using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketTracker.Application.Abstractions;
using TicketTracker.Infrastructure.Email;
using TicketTracker.Infrastructure.Security;
using TicketTracker.Infrastructure.Time;

namespace TicketTracker.Infrastructure;

/// <summary>
/// Registers Infrastructure adapters (ports → implementations). Persistence is registered
/// separately via <c>AddAppPersistence</c> so it stays swappable for tests (ADR-0002).
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    // Built-in pepper used ONLY outside Production (Development/Testing) so the existing test
    // suite runs without configuring AUTH_TOKEN_SECRET. Production fails fast instead (below).
    private const string DevelopmentTokenSecret = "ticket-tracker-dev-token-secret-not-for-production";

    /// <param name="isProduction">
    /// True only when the host environment is Production. Controls AUTH_TOKEN_SECRET handling:
    /// Production fails fast on a missing/empty secret; other environments fall back to a
    /// built-in dev key so tests need no extra configuration.
    /// </param>
    public static IServiceCollection AddInfrastructureAdapters(
        this IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();

        // Keyed token hashing pepper (AUTH-004 / SCT-005). The documented AUTH_TOKEN_SECRET is
        // now actually consumed: HMAC-SHA256 keys the stored token hash with it.
        var tokenSecret = configuration["AUTH_TOKEN_SECRET"];
        if (string.IsNullOrWhiteSpace(tokenSecret))
        {
            if (isProduction)
                throw new InvalidOperationException(
                    "AUTH_TOKEN_SECRET is required in Production but was not configured. " +
                    "Set a strong random value (see .env.example / ARCHITECTURE §8).");
            tokenSecret = DevelopmentTokenSecret;
        }
        services.AddSingleton<ITokenGenerator>(_ => new CryptoTokenGenerator(tokenSecret));

        // SMTP options are bound from the SMTP_* environment keys in the API composition root
        // (Program.cs). No appsettings "Smtp" section exists, so no binding is registered here.
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
