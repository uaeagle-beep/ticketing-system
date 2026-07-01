namespace TicketTracker.Domain.Enums;

/// <summary>
/// Canonical application-event codes for the Wave 2 event backbone (ADR-0012, WAVE2_DESIGN §6.1).
/// Both <c>notifications.event_type</c> and <c>activity_entries.event_type</c> store these as
/// canonical lowercase text + a CHECK constraint (parity with ticket type/state, ADR-0002).
/// The event is raised explicitly by the mutating service AFTER its SaveChanges commits and is
/// consumed by the two in-process handlers (ActivityRecorder, NotificationFanout).
/// </summary>
public enum EventType
{
    /// <summary>A ticket was created. Notify + activity.</summary>
    TicketCreated,

    /// <summary>A scalar ticket field changed (one event per field: title/description/type/priority/due_date/epic/team). Notify + activity.</summary>
    TicketFieldChanged,

    /// <summary>The ticket's workflow state changed. Notify + activity.</summary>
    TicketMoved,

    /// <summary>The assignee set changed (carries added/removed in data_json). Notify + activity.</summary>
    TicketAssigneesChanged,

    /// <summary>A comment was added. Notify + activity.</summary>
    CommentAdded,

    /// <summary>A comment body was edited. Activity ONLY — no notification/email (ADR-0015).</summary>
    CommentEdited,

    /// <summary>A comment was deleted. Activity ONLY — no notification/email (ADR-0015).</summary>
    CommentDeleted,

    /// <summary>A ticket was deleted. Notify ONLY — its activity cascades away with the ticket (§6.1/§6.6).</summary>
    TicketDeleted,

    /// <summary>A file was attached to a ticket (Wave 3, ADR-0018). Notify + activity (like comment_added).</summary>
    AttachmentAdded,

    /// <summary>A file attachment was removed (Wave 3, ADR-0018). Activity ONLY (mirrors comment_deleted).</summary>
    AttachmentDeleted
}

/// <summary>
/// Single source of truth for the canonical string form of <see cref="EventType"/> and for the two
/// per-event policy flags (notifiable / activity-logged), mirroring <see cref="EnumCanonical"/>.
/// The canonical set is also emitted into the DB CHECK constraints (AppDbContext).
/// </summary>
public static class EventTypeCanonical
{
    public static string ToCanonical(EventType type) => type switch
    {
        EventType.TicketCreated => "ticket_created",
        EventType.TicketFieldChanged => "ticket_field_changed",
        EventType.TicketMoved => "ticket_moved",
        EventType.TicketAssigneesChanged => "ticket_assignees_changed",
        EventType.CommentAdded => "comment_added",
        EventType.CommentEdited => "comment_edited",
        EventType.CommentDeleted => "comment_deleted",
        EventType.TicketDeleted => "ticket_deleted",
        EventType.AttachmentAdded => "attachment_added",
        EventType.AttachmentDeleted => "attachment_deleted",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown event type.")
    };

    /// <summary>All canonical event-type codes, for the DB CHECK constraint (single source of truth).</summary>
    public static readonly IReadOnlyList<string> AllCanonical = new[]
    {
        "ticket_created",
        "ticket_field_changed",
        "ticket_moved",
        "ticket_assignees_changed",
        "comment_added",
        "comment_edited",
        "comment_deleted",
        "ticket_deleted",
        "attachment_added",
        "attachment_deleted"
    };

    /// <summary>The SQL <c>IN (...)</c> value list for the event_type CHECK constraint.</summary>
    public static string CheckConstraintValues()
        => string.Join(",", AllCanonical.Select(v => $"'{v}'"));

    /// <summary>
    /// True when the event fans out in-app notifications to watchers. Per ADR-0013/0015:
    /// comment edited/deleted are activity-only; everything else notifies.
    /// </summary>
    public static bool IsNotifiable(EventType type) => type switch
    {
        EventType.CommentEdited => false,
        EventType.CommentDeleted => false,
        // attachment_deleted is activity-only (audit-worthy, low-value to email); attachment_added notifies.
        EventType.AttachmentDeleted => false,
        _ => true
    };

    /// <summary>
    /// True when the event writes an <c>ActivityEntry</c>. Per WAVE2_DESIGN §6.1: everything except
    /// <see cref="EventType.TicketDeleted"/> (whose activity would cascade away with the ticket).
    /// </summary>
    public static bool IsActivityLogged(EventType type) => type switch
    {
        EventType.TicketDeleted => false,
        _ => true
    };
}
