namespace TicketTracker.Application.Abstractions;

/// <summary>
/// Abstraction over the system clock so tests can control time (modified_at semantics,
/// token expiry boundaries). Always returns UTC.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
