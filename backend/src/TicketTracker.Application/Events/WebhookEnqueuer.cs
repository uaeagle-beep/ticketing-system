using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketTracker.Application.Abstractions;
using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Events;

/// <summary>
/// The fourth <see cref="ITicketEventHandler"/> on the Wave-2 event backbone (Wave 3, ADR-0021, §6.3):
/// on each after-commit event it inserts one <see cref="WebhookDelivery"/> row (status <c>pending</c>,
/// <c>next_attempt_at = now</c>, <c>attempts = 0</c>, <c>payload_json = render(event)</c>) per ACTIVE team
/// subscription whose <c>event_types</c> matches the event (or is <c>"*"</c>). It writes rows ONLY (like
/// <see cref="NotificationFanout"/>) — the actual HTTP send is the delivery worker's job, so a slow/dead
/// subscriber never touches the request path. Owns its own <c>SaveChanges</c>; logs and swallows on failure
/// (never rethrows — the user's mutation already committed). Registered scoped alongside the other handlers.
/// </summary>
public sealed class WebhookEnqueuer : ITicketEventHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<WebhookEnqueuer> _logger;

    public WebhookEnqueuer(IAppDbContext db, IClock clock, ILogger<WebhookEnqueuer> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(IReadOnlyList<TicketEvent> events, CancellationToken ct)
    {
        if (events is null || events.Count == 0)
            return;

        try
        {
            var now = _clock.UtcNow;
            var any = false;

            // Group by ticket so we resolve each ticket's team + its active subscriptions once.
            foreach (var ticketGroup in events.GroupBy(e => e.TicketId))
            {
                var ticketId = ticketGroup.Key;

                // Resolve the ticket's team (AsNoTracking; the ticket is still live even for delete, §6.6).
                var teamId = await _db.Tickets.AsNoTracking()
                    .Where(t => t.Id == ticketId)
                    .Select(t => (Guid?)t.TeamId)
                    .FirstOrDefaultAsync(ct);
                if (teamId is null)
                    continue; // ticket already gone — nothing to deliver against

                // Active subscriptions of this team (their event-type filters are matched in-memory).
                var subscriptions = await _db.WebhookSubscriptions.AsNoTracking()
                    .Where(s => s.TeamId == teamId && s.Active)
                    .Select(s => new { s.Id, s.EventTypes })
                    .ToListAsync(ct);
                if (subscriptions.Count == 0)
                    continue;

                foreach (var e in ticketGroup)
                {
                    var eventCode = EventTypeCanonical.ToCanonical(e.Type);
                    foreach (var sub in subscriptions)
                    {
                        if (!Matches(sub.EventTypes, eventCode))
                            continue;

                        _db.WebhookDeliveries.Add(new WebhookDelivery
                        {
                            Id = Guid.NewGuid(),
                            SubscriptionId = sub.Id,
                            EventType = eventCode,
                            PayloadJson = RenderPayload(eventCode, e, now),
                            Status = WebhookDeliveryStatusCanonical.Pending,
                            Attempts = 0,
                            NextAttemptAt = now, // due immediately; the worker's clock drives sending
                            CreatedAt = now
                        });
                        any = true;
                    }
                }
            }

            if (any)
                await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue webhook deliveries for {Count} event(s).", events.Count);
        }
    }

    /// <summary>
    /// True when a subscription subscribes to <paramref name="eventCode"/>. <c>"*"</c> matches all; otherwise
    /// the stored value is a csv/whitespace-delimited list of canonical codes (case-insensitive, trimmed).
    /// </summary>
    private static bool Matches(string subscribedEventTypes, string eventCode)
    {
        var raw = subscribedEventTypes?.Trim();
        if (string.IsNullOrEmpty(raw))
            return false;
        if (raw == "*")
            return true;

        foreach (var token in raw.Split(new[] { ',', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            if (string.Equals(token.Trim(), eventCode, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Render the exact JSON bytes that will be signed + sent (§8, "rendered once at enqueue"). Shape:
    /// <c>{ event, occurredAt, ticketId, actorId, commentId?, data?, summary }</c>. The per-delivery id rides
    /// the <c>X-TicketTracker-Delivery</c> header (set by the dispatcher), not the body, so all deliveries of
    /// one event share identical bytes. <c>data</c> is the event's structured payload parsed back to a node so
    /// it nests as an object, not an escaped string.
    /// </summary>
    private static string RenderPayload(string eventCode, TicketEvent e, DateTime now)
    {
        object? data = null;
        if (!string.IsNullOrEmpty(e.DataJson))
        {
            try { data = JsonSerializer.Deserialize<JsonElement>(e.DataJson); }
            catch { data = null; }
        }

        return JsonSerializer.Serialize(new
        {
            @event = eventCode,
            occurredAt = now,
            ticketId = e.TicketId,
            actorId = e.ActorId,
            commentId = e.CommentId,
            data,
            summary = e.SummaryForActivity
        }, JsonOpts);
    }
}
