namespace TicketTracker.Domain.Entities;

/// <summary>
/// One outbound webhook delivery — the outbox row (Wave 3, ADR-0021, §4.4). Enqueued <c>pending</c> by
/// <c>WebhookEnqueuer</c> per matching active subscription; drained + signed + sent by the delivery
/// worker. <see cref="PayloadJson"/> is rendered ONCE at enqueue so the worker just signs+sends the exact
/// bytes. Retry uses <see cref="NextAttemptAt"/> (the outbox selector: <c>status='pending' AND
/// next_attempt_at &lt;= now</c>) with exponential backoff; terminal states clear it. The row id is the
/// <c>X-TicketTracker-Delivery</c> header value (subscriber idempotency key). Owned by the subscription
/// (CASCADE).
/// </summary>
public class WebhookDelivery
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }
    public WebhookSubscription? Subscription { get; set; }

    /// <summary>Canonical event-type code (or <c>webhook_ping</c> for a test delivery).</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The exact JSON bytes signed + sent (rendered once at enqueue).</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>Canonical outbox status: pending | delivered | failed (WebhookDeliveryStatusCanonical).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Number of send attempts made so far (incremented per try).</summary>
    public int Attempts { get; set; }

    /// <summary>The outbox selector; null once terminal (delivered/failed).</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>Last HTTP status observed (null if never reached the endpoint).</summary>
    public int? LastStatusCode { get; set; }

    /// <summary>Last failure reason (timeout, 5xx, ssrf_blocked, …); null on success.</summary>
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Set on the first 2xx.</summary>
    public DateTime? DeliveredAt { get; set; }
}
