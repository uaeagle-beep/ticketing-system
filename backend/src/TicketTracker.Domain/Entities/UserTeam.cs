namespace TicketTracker.Domain.Entities;

/// <summary>
/// Membership join between a <see cref="User"/> and a <see cref="Team"/> (ADR-0007). Many-to-many
/// modelled as an explicit entity (not an EF implicit join) so it carries <see cref="CreatedAt"/>
/// and can be queried directly. A user cannot belong to the same team twice — enforced by a unique
/// index on <c>(user_id, team_id)</c> (INV-1). Both FKs CASCADE: deleting a user or a team drops the
/// association rows (it is not authored content), but does NOT relax the Team→Ticket/Epic RESTRICT
/// guards (those FKs are unchanged). See USER_MANAGEMENT_DESIGN §2.2.
/// </summary>
public class UserTeam
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>When the membership was granted (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
