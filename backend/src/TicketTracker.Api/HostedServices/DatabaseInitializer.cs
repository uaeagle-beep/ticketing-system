using Microsoft.EntityFrameworkCore;
using TicketTracker.Infrastructure.Persistence;

namespace TicketTracker.Api.HostedServices;

/// <summary>
/// Applies EF Core migrations on startup with bounded retry (ADR-0003). The DB container may
/// still be starting even after the compose healthcheck gate, so we retry the connection before
/// calling <c>MigrateAsync()</c>. No seeding ever (V28) — the only non-schema rows are EF's
/// migration history. On success it flips the shared readiness flag so /health/ready turns ready.
/// Disabled when RUN_MIGRATIONS_ON_STARTUP=false (ops may apply migrations out-of-band).
/// </summary>
public sealed class DatabaseInitializer : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly DatabaseReadinessState _readiness;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IServiceProvider services,
        DatabaseReadinessState readiness,
        IConfiguration configuration,
        ILogger<DatabaseInitializer> logger)
    {
        _services = services;
        _readiness = readiness;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runMigrations = _configuration.GetValue("RUN_MIGRATIONS_ON_STARTUP", true);
        if (!runMigrations)
        {
            _logger.LogInformation("RUN_MIGRATIONS_ON_STARTUP is false; skipping auto-migration. Marking ready.");
            _readiness.MigrationsApplied = true;
            return;
        }

        const int maxAttempts = 30;
        var delay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; attempt <= maxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                await db.Database.MigrateAsync(stoppingToken);

                _readiness.MigrationsApplied = true;
                _logger.LogInformation("Database migrations applied successfully on attempt {Attempt}.", attempt);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex,
                    "Database not ready (attempt {Attempt}/{Max}); retrying in {Delay}s.",
                    attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
            }
        }

        // Final attempt outside the catch so a persistent failure crashes the host (fail-fast,
        // ADR-0003) rather than serving a half-migrated schema.
        if (!_readiness.MigrationsApplied && !stoppingToken.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(stoppingToken);
            _readiness.MigrationsApplied = true;
            _logger.LogInformation("Database migrations applied successfully on the final attempt.");
        }
    }
}
