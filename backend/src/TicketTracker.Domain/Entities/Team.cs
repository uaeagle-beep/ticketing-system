namespace TicketTracker.Domain.Entities;

/// <summary>
/// Grouping container for tickets and epics. Name is non-empty after trim and unique
/// case-insensitively via the normalized companion column (V8, EC2).
/// ModifiedAt advances only on a real rename of the team entity itself (A9, A10).
/// </summary>
public class Team
{
    public Guid Id { get; set; }

    /// <summary>Trimmed display name (non-empty).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>trim(lower(name)) — case-insensitive uniqueness key.</summary>
    public string NameNormalized { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    public ICollection<Epic> Epics { get; set; } = new List<Epic>();
    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();

    /// <summary>Per-state WIP caps for this team; an absent state means unlimited (V-WIP-1).</summary>
    public ICollection<WipLimit> WipLimits { get; set; } = new List<WipLimit>();

    /// <summary>Memberships granting users access to this team (ADR-0007).</summary>
    public ICollection<UserTeam> Members { get; set; } = new List<UserTeam>();
}
