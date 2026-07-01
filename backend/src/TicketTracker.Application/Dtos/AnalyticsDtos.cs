namespace TicketTracker.Application.Dtos;

// API_CONTRACT §5.4 (Wave 3, ADR-0020). The reporting dashboard is a single composite, read-only,
// team-scoped payload aggregated LIVE over existing tables + activity_entries (NO new tables). All
// counts/buckets are pre-aggregated server-side (≤ a few dozen numbers) so the SPA plots a small fixed
// number of points regardless of ticket volume (the "100+ tickets" NFR is met on the server, [ADR-0020]).
// Enum keys (state/priority/type) are the canonical lowercase codes used everywhere else (EnumCanonical).

/// <summary>
/// One team's reporting dashboard for an inclusive UTC calendar-day range (GET /api/analytics/dashboard).
/// Every metric is computed inside <c>WHERE team_id = @teamId</c> so no metric can leak another team's data
/// (§7.6). <paramref name="From"/>/<paramref name="To"/> echo the resolved range (defaults to the last 12
/// weeks when the caller omits them).
/// </summary>
public sealed record DashboardDto(
    Guid TeamId,
    DateOnly From,
    DateOnly To,
    // (a) tickets by workflow state — canonical state code → count (all five states always present).
    IReadOnlyDictionary<string, int> ByState,
    // (b) tickets by priority — canonical priority code → count (all four present).
    IReadOnlyDictionary<string, int> ByPriority,
    // (c) tickets by type — canonical type code → count (all three present).
    IReadOnlyDictionary<string, int> ByType,
    // (d) tickets by label — one row per label the team has (id + name + color for the chip/legend).
    IReadOnlyList<LabelCountDto> ByLabel,
    // (e) open vs done snapshot (open = every non-done state).
    OpenVsDoneDto OpenVsDone,
    // (f) throughput = tickets that reached `done` per ISO week, within the range (chronological).
    IReadOnlyList<ThroughputBucketDto> Throughput,
    // (g) cycle time (created_at → first reached `done`) over done tickets in the range.
    CycleTimeDto CycleTime,
    // (h) overdue count (dueDate < today, not done) — a live snapshot, not range-bounded.
    int OverdueCount,
    // (i) WIP vs limit per state (over-limit highlighted).
    IReadOnlyList<WipStateDto> Wip);

/// <summary>A per-label ticket count for the by-label chart (id + name + color to render the chip/legend).</summary>
public sealed record LabelCountDto(Guid LabelId, string Name, string Color, int Count);

/// <summary>Open (every non-done state) vs done snapshot for the whole team (§5.4 (e)).</summary>
public sealed record OpenVsDoneDto(int Open, int Done);

/// <summary>
/// Tickets that reached <c>done</c> during the ISO week beginning <paramref name="WeekStart"/> (Monday, UTC
/// calendar day). Throughput derives "reached done" from the Wave-2 <c>ticket_moved</c> activity entries,
/// falling back to <c>modified_at</c> for tickets with no such entry ([ASSUMPTION W3-AN-TIMING-SOURCE]).
/// </summary>
public sealed record ThroughputBucketDto(DateOnly WeekStart, int DoneCount);

/// <summary>
/// Cycle time in days (created_at → first reached done) over the done tickets whose "reached done" date falls
/// in the range. <paramref name="SampleSize"/> is how many done tickets contributed; avg/median are null when
/// the sample is empty (§5.4 (g)).
/// </summary>
public sealed record CycleTimeDto(double? AvgDays, double? MedianDays, int SampleSize);

/// <summary>
/// WIP-vs-limit for one state: the live count in the state, the team's configured cap (null = unlimited) and
/// whether the state is over its cap (§5.4 (i)). Emitted for every one of the five states in workflow order.
/// </summary>
public sealed record WipStateDto(string State, int Count, int? Limit, bool OverLimit);
