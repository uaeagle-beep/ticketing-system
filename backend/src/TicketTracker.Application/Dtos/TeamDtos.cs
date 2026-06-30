using System.Text.Json;

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
    DateTime ModifiedAt,
    // Per-state WIP caps for all five states; value is null when that state is unlimited (API_CONTRACT §4).
    IReadOnlyDictionary<string, int?> WipLimits);

/// <summary>
/// Body of PUT /api/teams/{id}/wip-limits: a map of canonical state -> limit. A value of null
/// (or an omitted state) means "no limit". An integer must be in [1, 999]; anything else (0, negative,
/// fractional, non-numeric, out of range, unknown state key) is rejected with 400 validation_error and
/// a per-state error keyed by the offending state.
/// <para>
/// Raw <see cref="JsonElement"/> values are accepted so the service — not the model binder — decides
/// validity. This keeps a fractional/non-numeric value a clean 400 validation_error in the uniform
/// envelope (ARCHITECTURE §3.3) instead of a binder ProblemDetails.
/// </para>
/// </summary>
public sealed record UpdateWipLimitsRequest(IReadOnlyDictionary<string, JsonElement>? WipLimits);
