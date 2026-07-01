using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Services;

/// <summary>
/// Reporting dashboard read (Wave 3, ADR-0020, API_CONTRACT §5.4). One composite, read-only, team-scoped
/// endpoint aggregating the ~nine Kanban-health metrics LIVE over existing tables + <c>activity_entries</c>
/// — NO new tables. Team-scoped: resolve team → 404, <c>RequireTeamAccess</c> → 403 (admin sees any, 404-then-403
/// anti-IDOR, ADR-0007). Every aggregate is computed inside <c>WHERE team_id = @teamId</c> so no metric can
/// leak another team's data (§7.6).
///
/// SQLite parity ([ADR-0002]): the grouped counts (state/priority/type/label/overdue/open-vs-done/WIP) run as
/// provider-agnostic EF <c>GroupBy</c>/<c>Count</c>. The time-based metrics (throughput per ISO week, cycle
/// time) avoid provider-specific date functions (Postgres <c>date_trunc</c>/<c>EXTRACT</c> vs SQLite) by
/// fetching a LEAN projection and computing the ISO-week bucketing + median in memory — correct and identical
/// on both providers. "When did a ticket reach done" derives from the Wave-2 <c>ticket_moved</c> activity
/// entries (<c>data_json {from,to}</c>), falling back to <c>modified_at</c> for tickets with no such entry
/// ([ASSUMPTION W3-AN-TIMING-SOURCE]). <c>IClock</c> supplies "today" for the overdue/default-range metrics.
/// </summary>
public sealed class AnalyticsService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Default range when the caller omits from/to: the last 12 weeks (84 days) ending today (§5.4).</summary>
    private const int DefaultRangeDays = 12 * 7;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public AnalyticsService(IAppDbContext db, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<DashboardDto> GetDashboardAsync(Guid? teamId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (teamId is null || teamId == Guid.Empty)
            throw ServiceException.Validation("teamId", "teamId is required.");

        // Resolve-then-check: unknown team → 404 before any access decision (anti-IDOR, §3.3 / ADR-0007).
        var team = teamId.Value;
        var teamExists = await _db.Teams.AnyAsync(t => t.Id == team, ct);
        if (!teamExists)
            throw ServiceException.NotFound("Team not found.");
        _currentUser.RequireTeamAccess(team);

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var (rangeFrom, rangeTo) = ResolveRange(from, to, today);

        // ----- Snapshot counts over the team's tickets (provider-agnostic GroupBy/Count) -----

        // (a) by state, (b) by priority, (c) by type — one grouped round-trip each, keyed by the stored enum.
        var byStateRaw = await _db.Tickets.AsNoTracking()
            .Where(t => t.TeamId == team)
            .GroupBy(t => t.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.State, g => g.Count, ct);

        var byPriorityRaw = await _db.Tickets.AsNoTracking()
            .Where(t => t.TeamId == team)
            .GroupBy(t => t.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Priority, g => g.Count, ct);

        var byTypeRaw = await _db.Tickets.AsNoTracking()
            .Where(t => t.TeamId == team)
            .GroupBy(t => t.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Type, g => g.Count, ct);

        var byState = BuildStateMap(byStateRaw);
        var byPriority = BuildPriorityMap(byPriorityRaw);
        var byType = BuildTypeMap(byTypeRaw);

        // (e) open (every non-done state) vs done — derived from the by-state totals (no extra round-trip).
        var doneCount = byStateRaw.TryGetValue(TicketState.Done, out var d) ? d : 0;
        var totalTickets = byStateRaw.Values.Sum();
        var openVsDone = new OpenVsDoneDto(totalTickets - doneCount, doneCount);

        // (d) by label — group the team's ticket-label tags; carry name + color for the chip/legend.
        var byLabel = await _db.TicketLabels.AsNoTracking()
            .Where(tl => tl.Label!.TeamId == team)
            .GroupBy(tl => new { tl.LabelId, tl.Label!.Name, tl.Label.Color, tl.Label.NameNormalized })
            .Select(g => new
            {
                g.Key.LabelId,
                g.Key.Name,
                g.Key.Color,
                g.Key.NameNormalized,
                Count = g.Count()
            })
            .OrderBy(g => g.NameNormalized)
            .Select(g => new LabelCountDto(g.LabelId, g.Name, g.Color, g.Count))
            .ToListAsync(ct);

        // (h) overdue = due before today, not done — a live snapshot (mirrors the board's isOverdue, §3.3).
        var overdueCount = await _db.Tickets.AsNoTracking()
            .Where(t => t.TeamId == team && t.DueDate != null && t.DueDate < today && t.State != TicketState.Done)
            .CountAsync(ct);

        // (i) WIP vs limit per state — reuse the board's unfiltered per-state totals + the team's caps.
        var wip = await BuildWipAsync(team, byStateRaw, ct);

        // ----- Time-based metrics: (f) throughput per ISO week, (g) cycle time -----
        // Compute in memory after a lean fetch to stay provider-agnostic (no date_trunc/EXTRACT).
        var (throughput, cycleTime) = await BuildTimingMetricsAsync(team, rangeFrom, rangeTo, ct);

        return new DashboardDto(
            team, rangeFrom, rangeTo,
            byState, byPriority, byType, byLabel,
            openVsDone, throughput, cycleTime, overdueCount, wip);
    }

    // ----- range -----

    /// <summary>
    /// Resolve the optional from/to to an inclusive UTC calendar-day range. Defaults to the last 12 weeks
    /// ending today when both are omitted; a partial range fills the missing side from the default; <c>from &gt; to</c>
    /// is a 400 (§5.4).
    /// </summary>
    private static (DateOnly From, DateOnly To) ResolveRange(DateOnly? from, DateOnly? to, DateOnly today)
    {
        var resolvedTo = to ?? today;
        var resolvedFrom = from ?? resolvedTo.AddDays(-DefaultRangeDays);
        if (resolvedFrom > resolvedTo)
            throw ServiceException.Validation("from", "The 'from' date must not be after the 'to' date.");
        return (resolvedFrom, resolvedTo);
    }

    // ----- snapshot map builders (all five/four/three keys always present, canonical codes) -----

    private static IReadOnlyDictionary<string, int> BuildStateMap(IReadOnlyDictionary<TicketState, int> raw)
    {
        var map = new Dictionary<string, int>(EnumCanonical.WorkflowOrder.Length, StringComparer.Ordinal);
        foreach (var state in EnumCanonical.WorkflowOrder)
            map[EnumCanonical.ToCanonical(state)] = raw.TryGetValue(state, out var c) ? c : 0;
        return map;
    }

    private static IReadOnlyDictionary<string, int> BuildPriorityMap(IReadOnlyDictionary<TicketPriority, int> raw)
    {
        var map = new Dictionary<string, int>(EnumCanonical.PriorityValues.Length, StringComparer.Ordinal);
        foreach (var priority in EnumCanonical.PriorityValues)
            map[EnumCanonical.ToCanonical(priority)] = raw.TryGetValue(priority, out var c) ? c : 0;
        return map;
    }

    private static IReadOnlyDictionary<string, int> BuildTypeMap(IReadOnlyDictionary<TicketType, int> raw)
    {
        var map = new Dictionary<string, int>(3, StringComparer.Ordinal);
        foreach (var type in new[] { TicketType.Bug, TicketType.Feature, TicketType.Fix })
            map[EnumCanonical.ToCanonical(type)] = raw.TryGetValue(type, out var c) ? c : 0;
        return map;
    }

    // ----- WIP vs limit -----

    private async Task<IReadOnlyList<WipStateDto>> BuildWipAsync(
        Guid team, IReadOnlyDictionary<TicketState, int> byState, CancellationToken ct)
    {
        var limitsByState = await _db.WipLimits.AsNoTracking()
            .Where(w => w.TeamId == team)
            .ToDictionaryAsync(w => w.State, w => w.MaxCount, ct);

        var wip = new List<WipStateDto>(EnumCanonical.WorkflowOrder.Length);
        foreach (var state in EnumCanonical.WorkflowOrder)
        {
            var code = EnumCanonical.ToCanonical(state);
            var count = byState.TryGetValue(state, out var c) ? c : 0;
            int? limit = limitsByState.TryGetValue(code, out var max) ? max : null;
            wip.Add(new WipStateDto(code, count, limit, limit is { } l && count > l));
        }
        return wip;
    }

    // ----- throughput + cycle time (in-memory, provider-agnostic) -----

    /// <summary>
    /// Derives "when did each ticket reach done" and computes throughput per ISO week + cycle time over the
    /// range. Fetches only what's needed (tickets: id/createdAt/state/modifiedAt; the team's <c>ticket_moved</c>
    /// activity entries), then buckets/aggregates in memory so no provider-specific date SQL is used. The
    /// "reached done" timestamp is the EARLIEST <c>ticket_moved</c> whose target state was <c>done</c>; if a
    /// currently-done ticket has no such entry (pre-Wave-2), <c>modified_at</c> is the fallback
    /// ([ASSUMPTION W3-AN-TIMING-SOURCE]).
    /// </summary>
    private async Task<(IReadOnlyList<ThroughputBucketDto> Throughput, CycleTimeDto CycleTime)> BuildTimingMetricsAsync(
        Guid team, DateOnly rangeFrom, DateOnly rangeTo, CancellationToken ct)
    {
        var tickets = await _db.Tickets.AsNoTracking()
            .Where(t => t.TeamId == team)
            .Select(t => new { t.Id, t.CreatedAt, t.State, t.ModifiedAt })
            .ToListAsync(ct);

        // The team's state-move activity entries (id-scoped to this team via the ticket join). We read the
        // canonical event code + data_json + created_at and pick the earliest move-to-done per ticket.
        var movedCode = EventTypeCanonical.ToCanonical(EventType.TicketMoved);
        var moves = await _db.ActivityEntries.AsNoTracking()
            .Where(a => a.EventType == movedCode && a.Ticket!.TeamId == team)
            .Select(a => new { a.TicketId, a.DataJson, a.CreatedAt })
            .ToListAsync(ct);

        // Earliest "moved to done" timestamp per ticket (from the activity data_json {from,to}).
        var reachedDoneByTicket = new Dictionary<Guid, DateTime>();
        foreach (var m in moves)
        {
            if (!IsMoveToDone(m.DataJson))
                continue;
            if (!reachedDoneByTicket.TryGetValue(m.TicketId, out var existing) || m.CreatedAt < existing)
                reachedDoneByTicket[m.TicketId] = m.CreatedAt;
        }

        var doneCanonical = TicketState.Done;
        var weekBuckets = new Dictionary<DateOnly, int>();
        var cycleDays = new List<double>();

        foreach (var t in tickets)
        {
            // A ticket "reached done" if it has a move-to-done entry, OR it is currently done (fallback).
            DateTime? reachedDone =
                reachedDoneByTicket.TryGetValue(t.Id, out var moveAt) ? moveAt
                : t.State == doneCanonical ? t.ModifiedAt
                : null;
            if (reachedDone is not { } doneAt)
                continue;

            var doneDay = DateOnly.FromDateTime(doneAt);
            if (doneDay < rangeFrom || doneDay > rangeTo)
                continue;

            // (f) throughput bucket = the Monday of the ISO week that contains the "reached done" day.
            var weekStart = IsoWeekStart(doneDay);
            weekBuckets[weekStart] = weekBuckets.TryGetValue(weekStart, out var n) ? n + 1 : 1;

            // (g) cycle time = created_at → reached done, in days (never negative; clamp at 0 for clock skew).
            var days = (doneAt - t.CreatedAt).TotalDays;
            cycleDays.Add(days < 0 ? 0 : days);
        }

        var throughput = weekBuckets
            .OrderBy(kv => kv.Key)
            .Select(kv => new ThroughputBucketDto(kv.Key, kv.Value))
            .ToList();

        var cycleTime = BuildCycleTime(cycleDays);
        return (throughput, cycleTime);
    }

    /// <summary>True when a <c>ticket_moved</c> data_json's target state is <c>done</c> (tolerant of malformed json).</summary>
    private static bool IsMoveToDone(string? dataJson)
    {
        if (string.IsNullOrEmpty(dataJson))
            return false;
        try
        {
            var move = JsonSerializer.Deserialize<MovedData>(dataJson, JsonOpts);
            return string.Equals(move?.To, EnumCanonical.ToCanonical(TicketState.Done), StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record MovedData(string? From, string? To);

    /// <summary>
    /// The Monday (UTC calendar day) that starts the ISO-8601 week containing <paramref name="day"/>. Computed
    /// in .NET (no DB date function) so throughput bucketing is identical under SQLite and Postgres.
    /// </summary>
    private static DateOnly IsoWeekStart(DateOnly day)
    {
        // ISO weeks start on Monday. DayOfWeek is Sunday=0..Saturday=6; map Monday→0 … Sunday→6.
        int offset = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-offset);
    }

    private static CycleTimeDto BuildCycleTime(List<double> samples)
    {
        if (samples.Count == 0)
            return new CycleTimeDto(null, null, 0);

        var avg = Math.Round(samples.Average(), 2, MidpointRounding.AwayFromZero);
        var median = Math.Round(Median(samples), 2, MidpointRounding.AwayFromZero);
        return new CycleTimeDto(avg, median, samples.Count);
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
