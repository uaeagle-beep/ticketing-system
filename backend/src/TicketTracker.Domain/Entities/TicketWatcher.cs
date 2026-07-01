namespace TicketTracker.Domain.Entities;

/// <summary>
/// A subscription join between a <see cref="Ticket"/> and a <see cref="User"/> (Wave 2, ADR-0013).
/// A watcher receives in-app notifications (and coalesced email) for the ticket's events, minus their
/// own actions. Auto-subscribed on: creating the ticket, being added as an assignee, adding a comment;
/// otherwise watch/unwatch is manual. A ticket cannot be watched twice by the same user — enforced by a
/// unique index on <c>(ticket_id, user_id)</c>. Unlike an assignment, a watch carries no authorship, so
/// BOTH FKs CASCADE (mirrors <see cref="UserTeam"/>): losing the watch on ticket- or user-delete is fine.
/// See WAVE2_DESIGN §4.2.
/// </summary>
public class TicketWatcher
{
    public Guid Id { get; set; }

    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>When the watch started (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
