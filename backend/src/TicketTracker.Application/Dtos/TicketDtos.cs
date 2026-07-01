namespace TicketTracker.Application.Dtos;

// API_CONTRACT §6. type/state/priority are kept as raw strings so the service can enforce STRICT
// enum parsing (unknown value => 400 validation_error), never silent coercion (ARCHITECTURE §3.3).

// Priority/dueDate/assigneeIds are appended (optional) so existing positional callers keep compiling.
// - Priority: null/omitted ⇒ default 'medium' on create; REQUIRED-in-body on edit (mirrors type/state).
// - DueDate: DateOnly? — System.Text.Json parses "YYYY-MM-DD"; null ⇒ no due date.
// - AssigneeIds: null/omitted ⇒ leave the assignee set untouched (Wave 1 uses the dedicated
//   PUT /tickets/{id}/assignees sub-resource as the primary assignment path; §4.2 R-10).

public sealed record CreateTicketRequest(
    Guid? TeamId,
    string? Type,
    string? Title,
    string? Body,
    Guid? EpicId,
    string? State,
    string? Priority = null,
    DateOnly? DueDate = null,
    IReadOnlyList<Guid>? AssigneeIds = null);

public sealed record UpdateTicketRequest(
    Guid? TeamId,
    string? Type,
    Guid? EpicId,
    string? Title,
    string? Body,
    string? State,
    string? Priority = null,
    DateOnly? DueDate = null,
    IReadOnlyList<Guid>? AssigneeIds = null);

public sealed record PatchTicketStateRequest(string? State);

/// <summary>Full-set replace of a ticket's assignees (API_CONTRACT §4.2). Authoritative complete set.</summary>
public sealed record SetAssigneesRequest(IReadOnlyList<Guid>? UserIds);

/// <summary>
/// A lightweight assignee reference (id + display name) for ticket detail/card (API_CONTRACT §4.2).
/// <c>DisplayName = name?.trim() || email</c> computed server-side; the SPA never recomputes.
/// </summary>
public sealed record AssigneeRefDto(Guid Id, string DisplayName);

/// <summary>Full ticket detail (GET /tickets/{id}, POST, PUT).</summary>
public sealed record TicketDetailDto(
    Guid Id,
    Guid TeamId,
    Guid? EpicId,
    string? EpicTitle,
    string Type,
    string State,
    string Priority,
    string Title,
    string Body,
    DateOnly? DueDate,
    bool IsOverdue,
    IReadOnlyList<AssigneeRefDto> Assignees,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    Guid CreatedBy,
    string CreatedByEmail,
    string? CreatedByName);

/// <summary>Compact card payload used inside the board columns (API_CONTRACT §6.1).</summary>
public sealed record TicketCardDto(
    Guid Id,
    string Type,
    string State,
    string Priority,
    string Title,
    Guid? EpicId,
    string? EpicTitle,
    DateOnly? DueDate,
    bool IsOverdue,
    IReadOnlyList<AssigneeRefDto> Assignees,
    DateTime ModifiedAt);

/// <summary>
/// One board column. <paramref name="Count"/> is the POST-FILTER number of cards in
/// <paramref name="Tickets"/> (A23). <paramref name="Total"/> is the UNFILTERED per-state total for the
/// team — what the WIP badge "N / max" compares against, so a type/epic/search filter can't make a full
/// column look not-full (UX §3.1). <paramref name="WipLimit"/> is the cap for this state (null = unlimited).
/// </summary>
public sealed record BoardColumnDto(
    string State,
    int Count,
    int Total,
    int? WipLimit,
    IReadOnlyList<TicketCardDto> Tickets);

public sealed record BoardDto(Guid TeamId, int Total, IReadOnlyList<BoardColumnDto> Columns);

/// <summary>Minimal response for PATCH /tickets/{id}/state (API_CONTRACT §6.5).</summary>
public sealed record TicketStateDto(Guid Id, string State, DateTime ModifiedAt);
