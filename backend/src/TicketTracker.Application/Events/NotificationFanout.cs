using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketTracker.Application.Abstractions;
using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Events;

/// <summary>
/// Writes one <see cref="Notification"/> row per eligible watcher for each notifiable event (ADR-0013,
/// §6.4). Recipients = watchers of the ticket, MINUS the actor, MINUS any watcher who has lost team
/// access (blocked, or no longer a member of the ticket's team and not admin) — the read-side of the
/// team-scope rule (ADR-0007), so a notification is never delivered to someone who lost the right to
/// see the ticket. The <see cref="TicketWatcher"/> row of a stale watcher is preserved, not pruned.
/// The handler only writes rows (instant in-app); email is the outbox worker's job (§7). Owns its own
/// <c>SaveChangesAsync</c> and logs (does not rethrow) on failure.
/// </summary>
public sealed class NotificationFanout : ITicketEventHandler
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<NotificationFanout> _logger;

    public NotificationFanout(IAppDbContext db, IClock clock, ILogger<NotificationFanout> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(IReadOnlyList<TicketEvent> events, CancellationToken ct)
    {
        var notifiable = events.Where(e => EventTypeCanonical.IsNotifiable(e.Type)).ToList();
        if (notifiable.Count == 0)
            return;

        try
        {
            var now = _clock.UtcNow;
            var any = false;

            foreach (var e in notifiable)
            {
                // The ticket may already be gone for ticket_deleted only if the delete happened first;
                // by design (§6.6) we publish delete BEFORE removing the ticket, so the team resolves.
                var teamId = await _db.Tickets.AsNoTracking()
                    .Where(t => t.Id == e.TicketId)
                    .Select(t => (Guid?)t.TeamId)
                    .FirstOrDefaultAsync(ct);
                if (teamId is null)
                    continue; // ticket already gone — nothing to fan out against

                // Eligible watchers = watchers(ticket) minus actor, minus blocked, and still having team
                // access (admin OR a member of the ticket's team). Single query (R-5).
                var recipientIds = await _db.TicketWatchers.AsNoTracking()
                    .Where(w => w.TicketId == e.TicketId && w.UserId != e.ActorId)
                    .Join(_db.Users, w => w.UserId, u => u.Id, (w, u) => u)
                    .Where(u => !u.IsBlocked
                                && (u.IsAdmin || u.Memberships.Any(m => m.TeamId == teamId)))
                    .Select(u => u.Id)
                    .ToListAsync(ct);

                foreach (var recipientId in recipientIds)
                {
                    _db.Notifications.Add(new Notification
                    {
                        Id = Guid.NewGuid(),
                        RecipientId = recipientId,
                        ActorId = e.ActorId,
                        TicketId = e.TicketId,
                        CommentId = e.CommentId,
                        EventType = EventTypeCanonical.ToCanonical(e.Type),
                        Summary = e.SummaryForNotification,
                        DataJson = e.DataJson,
                        CreatedAt = now,
                        ReadAt = null,
                        EmailedAt = null
                    });
                    any = true;
                }
            }

            if (any)
                await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fan out notifications for {Count} event(s).", notifiable.Count);
        }
    }
}
