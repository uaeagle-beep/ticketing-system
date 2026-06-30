namespace TicketTracker.Domain.Entities;

/// <summary>
/// A larger work item belonging to exactly one team. Team is chosen at creation and is
/// immutable thereafter (FR-E3-1, A13). Title is non-empty after trim; not unique (A11).
/// ModifiedAt advances only on a real title/description change (A14).
/// </summary>
public class Epic
{
    public Guid Id { get; set; }

    public Guid TeamId { get; set; }
    public Team? Team { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Optional, nullable.</summary>
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
}
