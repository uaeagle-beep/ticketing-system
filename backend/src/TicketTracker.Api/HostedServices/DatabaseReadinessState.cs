namespace TicketTracker.Api.HostedServices;

/// <summary>
/// Shared readiness flag (ADR-0003, API_CONTRACT §8.2). The <see cref="DatabaseInitializer"/>
/// flips <see cref="MigrationsApplied"/> to true once migrations finish; /health/ready reports
/// not-ready until then.
/// </summary>
public sealed class DatabaseReadinessState
{
    private volatile bool _migrationsApplied;

    public bool MigrationsApplied
    {
        get => _migrationsApplied;
        set => _migrationsApplied = value;
    }
}
