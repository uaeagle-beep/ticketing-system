namespace TicketTracker.Domain.Entities;

/// <summary>
/// An in-app notification row addressed to a recipient (Wave 2, ADR-0013). Created instantly by the
/// <c>NotificationFanout</c> handler for every eligible watcher (minus the actor) of a notifiable event.
/// Stores BOTH a structured payload (<see cref="EventType"/>, ids, <see cref="DataJson"/>) AND a
/// pre-rendered <see cref="Summary"/> so the list read is cheap and the email builder trivial.
/// <para>
/// The <see cref="EmailedAt"/> column doubles as the email outbox marker + idempotency key (ADR-0014):
/// null means "not yet emailed". <see cref="ReadAt"/> null means "unread".
/// </para>
/// <para>
/// Cascades (§4.1/§4.3): recipient CASCADE, actor RESTRICT (preserve "who did it"), <b>ticket_id is
/// NULLABLE with ON DELETE SET NULL</b> so a <c>ticket_deleted</c> notification OUTLIVES its ticket
/// (the summary is self-contained; the SPA renders a null <see cref="TicketId"/> as a non-navigable
/// tombstone). <see cref="CommentId"/> is FK-less so a comment delete neither cascade-nukes nor blocks it.
/// </para>
/// </summary>
public class Notification
{
    public Guid Id { get; set; }

    /// <summary>Owner of the row (FK users CASCADE).</summary>
    public Guid RecipientId { get; set; }
    public User? Recipient { get; set; }

    /// <summary>Who caused the event (FK users RESTRICT).</summary>
    public Guid ActorId { get; set; }
    public User? Actor { get; set; }

    /// <summary>Subject ticket. Nullable + ON DELETE SET NULL so the row survives a ticket delete (§6.6).</summary>
    public Guid? TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    /// <summary>Present for comment events. Intentionally FK-less (§4.3) — a dangling id is harmless.</summary>
    public Guid? CommentId { get; set; }

    /// <summary>Canonical event-type code (<see cref="Enums.EventTypeCanonical"/>); CHECK-constrained.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Human line rendered once at fan-out time (W2-NOTIF-RENDER).</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Small structured payload, e.g. <c>{"from":"new","to":"in_progress"}</c> (nullable).</summary>
    public string? DataJson { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Null = unread.</summary>
    public DateTime? ReadAt { get; set; }

    /// <summary>Null = not yet emailed (outbox marker + idempotency key, ADR-0014).</summary>
    public DateTime? EmailedAt { get; set; }
}
