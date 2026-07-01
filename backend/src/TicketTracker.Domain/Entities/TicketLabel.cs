namespace TicketTracker.Domain.Entities;

/// <summary>
/// Tag join between a <see cref="Ticket"/> and a <see cref="Label"/> (Wave 2, ADR-0016). Many-to-many
/// modelled as an explicit entity (like <see cref="TicketAssignee"/>) so it carries <see cref="CreatedAt"/>
/// and can be queried/diffed directly. A ticket cannot list the same label twice — enforced by a unique
/// index on <c>(ticket_id, label_id)</c>. BOTH FKs CASCADE (a tag is not standalone content — mirrors
/// Ticket→TicketAssignee; removing a label removes it from all tickets, §4.8). Invariant: a label may only
/// tag a ticket OF THE LABEL'S OWN TEAM — enforced in <c>TicketService.SetLabelsAsync</c> (400 keyed
/// <c>labelIds</c>), never persisted for a cross-team label.
/// </summary>
public class TicketLabel
{
    public Guid Id { get; set; }

    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid LabelId { get; set; }
    public Label? Label { get; set; }

    /// <summary>When the tag was applied (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
