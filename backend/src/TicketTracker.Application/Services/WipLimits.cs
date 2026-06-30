using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Services;

/// <summary>
/// Shared WIP-limit helpers used by both <see cref="TeamService"/> (read/write) and
/// <see cref="TicketService"/> (enforcement). Keeps the canonical block message and the
/// "all five states, null = unlimited" map shape in one place so they cannot drift.
/// </summary>
internal static class WipLimits
{
    /// <summary>
    /// Product-mandated rejection message shown on drag / create / edit into a full state
    /// (UX §4.1). Returned with <see cref="Common.ServiceErrorCode.WipLimitReached"/> (409).
    /// </summary>
    public const string ReachedMessage =
        "This status already has the maximum number of tickets — finish existing ones first.";

    /// <summary>
    /// Build the full per-state map for the API: every one of the five states is present, with
    /// the team's configured cap or null when that state is unlimited (API_CONTRACT §4).
    /// </summary>
    public static IReadOnlyDictionary<string, int?> ToMap(IEnumerable<(string State, int MaxCount)> limits)
    {
        var byState = limits.ToDictionary(l => l.State, l => (int?)l.MaxCount, StringComparer.Ordinal);
        var map = new Dictionary<string, int?>(EnumCanonical.WorkflowOrder.Length, StringComparer.Ordinal);
        foreach (var state in EnumCanonical.WorkflowOrder)
        {
            var key = EnumCanonical.ToCanonical(state);
            map[key] = byState.TryGetValue(key, out var max) ? max : null;
        }
        return map;
    }

    /// <summary>Convenience overload for materialized <see cref="WipLimit"/> entities.</summary>
    public static IReadOnlyDictionary<string, int?> ToMap(IEnumerable<WipLimit> limits)
        => ToMap(limits.Select(l => (l.State, l.MaxCount)));
}
