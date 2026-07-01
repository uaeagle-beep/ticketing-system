namespace TicketTracker.Application.Dtos;

// API_CONTRACT §8 (Wave 2, ADR-0012). Per-ticket activity timeline, team-scoped read.

/// <summary>One line in a ticket's activity timeline.</summary>
public sealed record ActivityEntryDto(
    Guid Id,
    string EventType,
    string Summary,
    Guid ActorId,
    string ActorDisplayName,
    DateTime CreatedAt);

/// <summary>A page of activity entries newest-first with keyset pagination (same cursor scheme as notifications).</summary>
public sealed record ActivityListDto(
    IReadOnlyList<ActivityEntryDto> Items,
    bool HasMore,
    string? NextCursor);
