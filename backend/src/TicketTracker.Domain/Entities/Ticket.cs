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

    /// <summary>
    /// Severity level (F-03, ADR-0009). Defaults to <see cref="TicketPriority.Medium"/> so a value
    /// always exists even if a code path forgets to set it; the service sets it explicitly on create.
    /// Filter + badge only — does NOT affect board ordering (A22).
    /// </summary>
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    /// <summary>Optional calendar-day deadline interpreted as UTC (F-08, ADR-0009). Null = no due date.</summary>
    public DateOnly? DueDate { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    /// <summary>Server-set from the authenticated user; immutable (V18, A16).</summary>
    public Guid CreatedBy { get; set; }
    public User? CreatedByUser { get; set; }

    public ICollection<Comment> Comments { get; set; } = new List<Comment>();

    /// <summary>Assignees (F-02, M:N via <see cref="TicketAssignee"/>). Cascade-deleted with the ticket.</summary>
    public ICollection<TicketAssignee> Assignees { get; set; } = new List<TicketAssignee>();

    /// <summary>Watchers (Wave 2, M:N via <see cref="TicketWatcher"/>). Cascade-deleted with the ticket.</summary>
    public ICollection<TicketWatcher> Watchers { get; set; } = new List<TicketWatcher>();

    /// <summary>Labels/tags (Wave 2, M:N via <see cref="TicketLabel"/>). Cascade-deleted with the ticket.</summary>
    public ICollection<TicketLabel> Labels { get; set; } = new List<TicketLabel>();
}
