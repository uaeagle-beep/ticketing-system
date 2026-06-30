namespace TicketTracker.Application.Dtos;

// API_CONTRACT §5

public sealed record CreateEpicRequest(Guid? TeamId, string? Title, string? Description);

// Team is read-only on edit (any teamId in the body is ignored) — US-EPIC-2.
public sealed record UpdateEpicRequest(string? Title, string? Description);

public sealed record EpicDto(
    Guid Id,
    Guid TeamId,
    string Title,
    string? Description,
    int TicketCount,
    DateTime CreatedAt,
    DateTime ModifiedAt);
