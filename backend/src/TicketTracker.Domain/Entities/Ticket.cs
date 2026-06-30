using TicketTracker.Domain.Enums;

namespace TicketTracker.Domain.Entities;

/// <summary>
/// The core Kanban work item. Type/State are stored as canonical lowercase text via EF
/// value converters (ARCHITECTURE §4.2). An epic, if referenced, must belong to the same
/// team as the ticket (V16) — enforced in the service on every create/update.
/// ModifiedAt advances ONLY on a real field/state change (V19/V20), never on comment add (V21).
/// </summary>
public class Ticket
{
    public Guid Id { get; set; }

    public Guid TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>Null OR an epic of the SAME team (V16).</summary>
    public Guid? EpicId { get; set; }
    public Epic? Epic { get; set; }

    public TicketType Type { get; set; }
    public TicketState State { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    /// <summary>Server-set from the authenticated user; immutable (V18, A16).</summary>
    public Guid CreatedBy { get; set; }
    public User? CreatedByUser { get; set; }

    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
}
