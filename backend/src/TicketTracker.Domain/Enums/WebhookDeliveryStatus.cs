namespace TicketTracker.Domain.Enums;

/// <summary>
/// Outbox state of a <c>webhook_deliveries</c> row (Wave 3, ADR-0021, §4.4). Canonical lowercase text +
/// a DB CHECK (parity with the ticket/event enums, ADR-0002). The drain selects
/// <c>status='pending' AND next_attempt_at &lt;= now</c>; a 2xx moves it to <c>delivered</c>; exhausting
/// the retry budget moves it to <c>failed</c> (kept for audit). Terminal states clear <c>next_attempt_at</c>.
/// </summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Awaiting (or between) delivery attempts — the only status the drain selects.</summary>
    Pending,

    /// <summary>A delivery attempt returned HTTP 2xx.</summary>
    Delivered,

    /// <summary>The retry budget was exhausted without a 2xx (kept for audit; never re-sent).</summary>
    Failed
}

/// <summary>Single source of truth for the canonical string form of <see cref="WebhookDeliveryStatus"/>.</summary>
public static class WebhookDeliveryStatusCanonical
{
    public const string Pending = "pending";
    public const string Delivered = "delivered";
    public const string Failed = "failed";

    public static string ToCanonical(WebhookDeliveryStatus status) => status switch
    {
        WebhookDeliveryStatus.Pending => Pending,
        WebhookDeliveryStatus.Delivered => Delivered,
        WebhookDeliveryStatus.Failed => Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown webhook delivery status.")
    };

    /// <summary>All canonical status codes, for the DB CHECK constraint.</summary>
    public static readonly IReadOnlyList<string> AllCanonical = new[] { Pending, Delivered, Failed };

    /// <summary>The SQL <c>IN (...)</c> value list for the status CHECK constraint.</summary>
    public static string CheckConstraintValues()
        => string.Join(",", AllCanonical.Select(v => $"'{v}'"));
}
