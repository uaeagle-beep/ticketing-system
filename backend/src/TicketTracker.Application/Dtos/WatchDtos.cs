namespace TicketTracker.Application.Dtos;

// API_CONTRACT §8 (Wave 2, ADR-0013). Watch/unwatch a ticket, M(team of ticket).

/// <summary>A watcher reference (id + display name) for the ticket-detail "watchers" affordance.</summary>
public sealed record WatcherRefDto(Guid Id, string DisplayName);

/// <summary>Response to POST/DELETE /api/tickets/{id}/watch — the caller's own watching flag.</summary>
public sealed record WatchStatusDto(bool Watching);

/// <summary>Response to GET /api/tickets/{id}/watchers — the caller's flag + the full watcher list.</summary>
public sealed record WatchersDto(bool Watching, IReadOnlyList<WatcherRefDto> Watchers);
