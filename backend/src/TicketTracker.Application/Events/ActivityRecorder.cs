using Microsoft.Extensions.Logging;
using TicketTracker.Application.Abstractions;
using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Events;

/// <summary>
/// Writes one <see cref="ActivityEntry"/> per activity-logged event (ADR-0012, §6.5). Runs regardless
/// of whether anyone is watching — activity is the objective history, notifications are the subjective
/// feed. <c>ticket_deleted</c> is skipped (its activity would cascade away with the ticket, §6.1).
/// Owns its own <c>SaveChangesAsync</c> and logs (does not rethrow) on failure.
/// </summary>
public sealed class ActivityRecorder : ITicketEventHandler
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<ActivityRecorder> _logger;

    public ActivityRecorder(IAppDbContext db, IClock clock, ILogger<ActivityRecorder> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(IReadOnlyList<TicketEvent> events, CancellationToken ct)
    {
        var any = false;
        foreach (var e in events)
        {
            if (!EventTypeCanonical.IsActivityLogged(e.Type))
                continue;

            _db.ActivityEntries.Add(new ActivityEntry
            {
                Id = Guid.NewGuid(),
                TicketId = e.TicketId,
                ActorId = e.ActorId,
                EventType = EventTypeCanonical.ToCanonical(e.Type),
                Summary = e.SummaryForActivity,
                DataJson = e.DataJson,
                CreatedAt = _clock.UtcNow
            });
            any = true;
        }

        if (!any)
            return;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record {Count} activity entr(ies).", events.Count);
        }
    }
}
