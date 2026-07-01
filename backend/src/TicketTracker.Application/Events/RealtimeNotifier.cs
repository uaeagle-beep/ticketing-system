using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Application.Events;

/// <summary>
/// The FIFTH <see cref="ITicketEventHandler"/> on the Wave-2 event backbone (Wave 3, ADR-0019, §6.4):
/// on each after-commit event batch it pushes THIN "something-changed" signals to the right SignalR groups
/// via the testable <see cref="IRealtimeNotifier"/> seam — <c>boardChanged(team)</c> for the board and
/// <c>ticketChanged(ticket, team)</c> for the open ticket detail page. It carries NO entity payloads
/// ([ASSUMPTION W3-RT-PAYLOAD]); the client reacts by invalidating its React Query keys and refetching
/// through the authorized REST path (so authz is re-checked on the read, not duplicated in the push).
///
/// Like the other handlers it depends ONLY on its seam (never on <c>IHubContext</c>), so its correctness is
/// asserted against a recording fake with no live socket ("correctness in a testable seam, transport in a
/// thin shell"). It resolves each ticket's team AsNoTracking, exactly as <see cref="NotificationFanout"/> /
/// <see cref="WebhookEnqueuer"/> do. It logs and swallows on failure (never rethrows — the user's mutation
/// already committed; a dropped signal is harmless because polling backstops it, ADR-0019). The notification
/// bell ping (<c>user:{id}</c>) is emitted by <see cref="NotificationFanout"/> after it inserts rows, since
/// it already knows the exact recipients (§6.4 decision).
/// </summary>
public sealed class RealtimeNotifier : ITicketEventHandler
{
    private readonly IAppDbContext _db;
    private readonly IRealtimeNotifier _rt;
    private readonly ILogger<RealtimeNotifier> _logger;

    public RealtimeNotifier(IAppDbContext db, IRealtimeNotifier rt, ILogger<RealtimeNotifier> logger)
    {
        _db = db;
        _rt = rt;
        _logger = logger;
    }

    public async Task HandleAsync(IReadOnlyList<TicketEvent> events, CancellationToken ct)
    {
        if (events is null || events.Count == 0)
            return;

        try
        {
            // Group by ticket so each ticket's team is resolved once and the board signal for a team is
            // pushed once per batch even if several events touched the same ticket.
            foreach (var ticketGroup in events.GroupBy(e => e.TicketId))
            {
                var ticketId = ticketGroup.Key;

                // Resolve the ticket's team (AsNoTracking; still live even for delete, §6.6).
                var teamId = await _db.Tickets.AsNoTracking()
                    .Where(t => t.Id == ticketId)
                    .Select(t => (Guid?)t.TeamId)
                    .FirstOrDefaultAsync(ct);
                if (teamId is null)
                    continue; // ticket already gone — nothing to signal against

                // One board signal for the team, one ticket signal for the open detail page. Thin signals
                // only; the client refetches through the authorized REST endpoint.
                await _rt.BoardChangedAsync(teamId.Value, ct);
                await _rt.TicketChangedAsync(ticketId, teamId.Value, ct);
            }
        }
        catch (Exception ex)
        {
            // Never rethrow: the user's mutation already committed (at-most-once, R-2). A dropped signal is
            // harmless — the SPA's throttled polling backstops it and the next event re-syncs (ADR-0019).
            _logger.LogError(ex, "Failed to push real-time signals for {Count} event(s).", events.Count);
        }
    }
}
