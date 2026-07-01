namespace TicketTracker.Domain.Entities;

/// <summary>
/// One line in a ticket's user-facing activity timeline (Wave 2, ADR-0012). Written by the
/// <c>ActivityRecorder</c> handler for every activity-logged event. This is the objective history of
/// the ticket (readable by any member of the ticket's team) and is a SEPARATE concern from any SEC-3
/// security/admin audit log (§7bis). The timeline belongs to the ticket: <c>ticket_id</c> CASCADE (it
/// dies with the ticket, which is why <c>ticket_deleted</c> writes no activity), <c>actor_id</c> RESTRICT
/// (preserve audit integrity). See WAVE2_DESIGN §4.5.
/// </summary>
public class ActivityEntry
{
    public Guid Id { get; set; }

    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid ActorId { get; set; }
    public User? Actor { get; set; }

    /// <summary>Canonical event-type code (<see cref="Enums.EventTypeCanonical"/>); CHECK-constrained.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Human line rendered at record time.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Structured before/after, e.g. <c>{"field":"priority","from":"low","to":"high"}</c> (nullable).</summary>
    public string? DataJson { get; set; }

    public DateTime CreatedAt { get; set; }
}
