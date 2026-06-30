namespace TicketTracker.Domain.Enums;

/// <summary>
/// Single source of truth for converting the domain enums to/from their
/// canonical lowercase API/DB string values (ARCHITECTURE §4.2, API_CONTRACT §9).
/// Used by EF value converters, DTO mapping and request parsing so the same
/// canonical strings are produced and accepted everywhere. Parsing is strict:
/// an unknown string returns false (callers map that to 400 validation_error).
/// </summary>
public static class EnumCanonical
{
    public static string ToCanonical(TicketType type) => type switch
    {
        TicketType.Bug => "bug",
        TicketType.Feature => "feature",
        TicketType.Fix => "fix",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown ticket type.")
    };

    public static string ToCanonical(TicketState state) => state switch
    {
        TicketState.New => "new",
        TicketState.ReadyForImplementation => "ready_for_implementation",
        TicketState.InProgress => "in_progress",
        TicketState.ReadyForAcceptance => "ready_for_acceptance",
        TicketState.Done => "done",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown ticket state.")
    };

    /// <summary>Strict parse. Only exact canonical lowercase values are accepted.</summary>
    public static bool TryParseType(string? value, out TicketType type)
    {
        switch (value)
        {
            case "bug": type = TicketType.Bug; return true;
            case "feature": type = TicketType.Feature; return true;
            case "fix": type = TicketType.Fix; return true;
            default: type = default; return false;
        }
    }

    /// <summary>Strict parse. Only exact canonical lowercase values are accepted.</summary>
    public static bool TryParseState(string? value, out TicketState state)
    {
        switch (value)
        {
            case "new": state = TicketState.New; return true;
            case "ready_for_implementation": state = TicketState.ReadyForImplementation; return true;
            case "in_progress": state = TicketState.InProgress; return true;
            case "ready_for_acceptance": state = TicketState.ReadyForAcceptance; return true;
            case "done": state = TicketState.Done; return true;
            default: state = default; return false;
        }
    }

    /// <summary>Workflow-ordered states used to render the five board columns (FR-E6-2).</summary>
    public static readonly TicketState[] WorkflowOrder =
    {
        TicketState.New,
        TicketState.ReadyForImplementation,
        TicketState.InProgress,
        TicketState.ReadyForAcceptance,
        TicketState.Done
    };
}
