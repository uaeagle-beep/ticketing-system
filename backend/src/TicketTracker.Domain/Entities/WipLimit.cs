namespace TicketTracker.Domain.Entities;

/// <summary>
/// A Work-In-Progress cap for one team in one board state. A team has at most one row per
/// state (UNIQUE(team_id, state)); the absence of a row means that state is unlimited.
/// State is stored as the canonical lowercase text used everywhere else (ARCHITECTURE §4.2),
/// e.g. "in_progress". MaxCount is a positive integer in [1, 999] enforced by TeamService.
/// Owned by its team and removed with it (ON DELETE CASCADE).
/// </summary>
public class WipLimit
{
    public Guid Id { get; set; }

    public Guid TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>Canonical lowercase board-state value (e.g. "in_progress").</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Maximum number of tickets allowed in this state for this team (>= 1).</summary>
    public int MaxCount { get; set; }
}
