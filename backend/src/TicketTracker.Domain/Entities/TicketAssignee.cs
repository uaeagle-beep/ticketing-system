namespace TicketTracker.Domain.Entities;

/// <summary>
/// Assignment join between a <see cref="Ticket"/> and a <see cref="User"/> (F-02, ADR-0009).
/// Many-to-many modelled as an explicit entity (like <see cref="UserTeam"/>) so it carries
/// <see cref="CreatedAt"/> and can be queried/diffed directly (Wave-2 notification readiness).
/// A ticket cannot list the same user twice — enforced by a unique index on
/// <c>(ticket_id, user_id)</c> (INV-W1). <c>ticket_id</c> CASCADE (an assignment is not standalone
/// content — mirrors Ticket→Comment); <c>user_id</c> RESTRICT (mirrors <c>created_by</c>; no
/// user-delete in scope). See WAVE1_DESIGN §3.2.
/// </summary>
public class TicketAssignee
{
    public Guid Id { get; set; }

    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>When the assignment was made (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
