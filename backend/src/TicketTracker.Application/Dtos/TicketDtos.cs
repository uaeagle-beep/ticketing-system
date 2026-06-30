namespace TicketTracker.Application.Dtos;

// API_CONTRACT §6. type/state are kept as raw strings so the service can enforce STRICT
// enum parsing (unknown value => 400 validation_error), never silent coercion (ARCHITECTURE §3.3).

public sealed record CreateTicketRequest(
    Guid? TeamId,
    string? Type,
    string? Title,
    string? Body,
    Guid? EpicId,
    string? State);

public sealed record UpdateTicketRequest(
    Guid? TeamId,
    string? Type,
    Guid? EpicId,
    string? Title,
    string? Body,
    string? State);

public sealed record PatchTicketStateRequest(string? State);

/// <summary>Full ticket detail (GET /tickets/{id}, POST, PUT).</summary>
public sealed record TicketDetailDto(
    Guid Id,
    Guid TeamId,
    Guid? EpicId,
    string? EpicTitle,
    string Type,
    string State,
    string Title,
    string Body,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    Guid CreatedBy,
    string CreatedByEmail);

/// <summary>Compact card payload used inside the board columns (API_CONTRACT §6.1).</summary>
public sealed record TicketCardDto(
    Guid Id,
    string Type,
    string State,
    string Title,
    Guid? EpicId,
    string? EpicTitle,
    DateTime ModifiedAt);

public sealed record BoardColumnDto(string State, int Count, IReadOnlyList<TicketCardDto> Tickets);

public sealed record BoardDto(Guid TeamId, int Total, IReadOnlyList<BoardColumnDto> Columns);

/// <summary>Minimal response for PATCH /tickets/{id}/state (API_CONTRACT §6.5).</summary>
public sealed record TicketStateDto(Guid Id, string State, DateTime ModifiedAt);
