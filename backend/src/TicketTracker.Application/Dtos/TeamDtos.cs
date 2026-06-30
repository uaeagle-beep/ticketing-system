namespace TicketTracker.Application.Dtos;

// API_CONTRACT §4

public sealed record CreateTeamRequest(string? Name);

public sealed record UpdateTeamRequest(string? Name);

public sealed record TeamDto(
    Guid Id,
    string Name,
    int TicketCount,
    int EpicCount,
    DateTime CreatedAt,
    DateTime ModifiedAt);
