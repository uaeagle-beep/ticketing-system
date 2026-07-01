using System.Text.Json;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Events;

/// <summary>
/// Server-side rendering of the human summary line and the structured <c>data_json</c> for each
/// application event (W2-NOTIF-RENDER, §6.1). The summary is rendered ONCE by the raising service so
/// the notification list read is cheap and the email builder trivial. Enum values are display-cased
/// (a small mirror of the SPA's <c>lib/labels</c>) so the email and timeline read naturally without
/// client help. The <c>data_json</c> keeps the canonical values for possible future render-on-read.
/// </summary>
public static class EventSummaries
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ----- display-cased label maps (mirror of frontend lib/labels) -----

    public static string StateLabel(TicketState state) => state switch
    {
        TicketState.New => "New",
        TicketState.ReadyForImplementation => "Ready for implementation",
        TicketState.InProgress => "In progress",
        TicketState.ReadyForAcceptance => "Ready for acceptance",
        TicketState.Done => "Done",
        _ => EnumCanonical.ToCanonical(state)
    };

    public static string TypeLabel(TicketType type) => type switch
    {
        TicketType.Bug => "Bug",
        TicketType.Feature => "Feature",
        TicketType.Fix => "Fix",
        _ => EnumCanonical.ToCanonical(type)
    };

    public static string PriorityLabel(TicketPriority priority) => priority switch
    {
        TicketPriority.Low => "Low",
        TicketPriority.Medium => "Medium",
        TicketPriority.High => "High",
        TicketPriority.Urgent => "Urgent",
        _ => EnumCanonical.ToCanonical(priority)
    };

    /// <summary>Human label for a tracked scalar field (used in the "changed {field}" summary).</summary>
    public static string FieldLabel(string field) => field switch
    {
        "title" => "title",
        "description" => "description",
        "type" => "type",
        "priority" => "priority",
        "due_date" => "due date",
        "epic" => "epic",
        "team" => "team",
        _ => field
    };

    // ----- summaries -----

    public static string TicketCreated(string actor) => $"{actor} created this ticket";

    public static string TicketMoved(string actor, TicketState from, TicketState to)
        => $"{actor} moved this from {StateLabel(from)} to {StateLabel(to)}";

    public static string TicketDeleted(string actor, string title)
        => $"{actor} deleted ticket '{title}'";

    public static string CommentAdded(string actor) => $"{actor} commented";
    public static string CommentEdited(string actor) => $"{actor} edited a comment";
    public static string CommentDeleted(string actor) => $"{actor} deleted a comment";

    /// <summary>"{actor} changed {field} from {from} to {to}" with human display values.</summary>
    public static string FieldChanged(string actor, string field, string? fromDisplay, string? toDisplay)
    {
        var from = string.IsNullOrEmpty(fromDisplay) ? "(none)" : fromDisplay;
        var to = string.IsNullOrEmpty(toDisplay) ? "(none)" : toDisplay;
        return $"{actor} changed {FieldLabel(field)} from {from} to {to}";
    }

    public static string AssigneesChanged(string actor, int added, int removed)
        => $"{actor} updated assignees (+{added} / -{removed})";

    // ----- data_json builders (canonical values) -----

    public static string FieldChangedData(string field, string? from, string? to)
        => JsonSerializer.Serialize(new { field, from, to }, JsonOpts);

    public static string MovedData(TicketState from, TicketState to)
        => JsonSerializer.Serialize(
            new { from = EnumCanonical.ToCanonical(from), to = EnumCanonical.ToCanonical(to) }, JsonOpts);

    public static string AssigneesData(IReadOnlyCollection<Guid> added, IReadOnlyCollection<Guid> removed)
        => JsonSerializer.Serialize(new { added, removed }, JsonOpts);

    public static string TitleData(string title)
        => JsonSerializer.Serialize(new { title }, JsonOpts);
}
