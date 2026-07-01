using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Abstractions;

/// <summary>
/// Explicit application-event publisher (ADR-0012). Mutating services call
/// <see cref="PublishAsync"/> AFTER their <c>SaveChangesAsync</c> has committed, so the in-process
/// handlers see committed state and a handler failure cannot roll back the user's mutation. This is
/// deliberately NOT an EF SaveChanges interceptor — emission is explicit, greppable and testable.
/// </summary>
public interface IDomainEventPublisher
{
    /// <summary>
    /// Publish events to all in-process handlers, synchronously, within the caller's scope. Each
    /// handler owns its own inserts and logs (does not rethrow) on failure — the user's mutation has
    /// already committed. A null/empty list is a no-op.
    /// </summary>
    Task PublishAsync(IReadOnlyList<TicketEvent> events, CancellationToken ct);
}

/// <summary>
/// One application event about a ticket (ADR-0012). Built by the raising service, which renders the
/// human summaries once (W2-NOTIF-RENDER) and computes the structured <see cref="DataJson"/>.
/// </summary>
/// <param name="Type">Canonical event type.</param>
/// <param name="TicketId">The subject ticket (still live when published, even for delete — §6.6).</param>
/// <param name="ActorId">Who caused the event — always excluded from notification fan-out.</param>
/// <param name="CommentId">Present for comment events; null otherwise.</param>
/// <param name="DataJson">Small structured payload (field/from/to, added/removed, title…), or null.</param>
/// <param name="SummaryForActivity">Rendered line for the activity timeline.</param>
/// <param name="SummaryForNotification">Rendered line for the notification (usually identical).</param>
public sealed record TicketEvent(
    EventType Type,
    Guid TicketId,
    Guid ActorId,
    Guid? CommentId,
    string? DataJson,
    string SummaryForActivity,
    string SummaryForNotification);
