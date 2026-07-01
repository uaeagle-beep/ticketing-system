using TicketTracker.Application.Abstractions;

namespace TicketTracker.Application.Events;

/// <summary>
/// An in-process consumer of the application-event backbone (ADR-0012). The two implementations are
/// <see cref="ActivityRecorder"/> (writes ActivityEntry rows) and <see cref="NotificationFanout"/>
/// (writes Notification rows for eligible watchers). A handler owns its own inserts + SaveChanges and
/// must NOT rethrow on failure — the user's mutation has already committed (the publisher logs).
/// </summary>
public interface ITicketEventHandler
{
    Task HandleAsync(IReadOnlyList<TicketEvent> events, CancellationToken ct);
}
