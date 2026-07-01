namespace TicketTracker.Application.Options;

/// <summary>
/// Outbound-webhook delivery tuning (Wave 3, ADR-0021, §8.3). Bound from environment in Program.cs, next to
/// <see cref="NotificationOptions"/>. All knobs are env-tunable so retry aggressiveness / timeout can change
/// without code. <see cref="AllowInsecure"/> is the local/test escape hatch that permits http:// targets and
/// skips the private-IP SSRF block — it MUST stay false in production.
/// </summary>
public sealed class WebhookOptions
{
    /// <summary>Master switch (env WEBHOOKS_ENABLED, default true). When false the worker returns immediately.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Drain tick interval in seconds (env WEBHOOK_WORKER_POLL_SECONDS, default 10).</summary>
    public int WorkerPollSeconds { get; set; } = 10;

    /// <summary>Attempts before a delivery is marked failed (env WEBHOOK_MAX_ATTEMPTS, default 5).</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Per-attempt HTTP timeout in seconds (env WEBHOOK_TIMEOUT_SECONDS, default 10).</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Local/test escape hatch (env WEBHOOKS_ALLOW_INSECURE, default false): allow http:// subscription URLs
    /// AND skip the send-time private-IP block. Default false = prod-safe. Tests set it (or use a fake sender)
    /// so SSRF checks don't block localhost targets.
    /// </summary>
    public bool AllowInsecure { get; set; }

    /// <summary>
    /// Max delivery rows drained per tick (a bound so a huge backlog can't monopolize one drain). Not env-bound;
    /// mirrors the email drain's "bounded, small scale" selection.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Exponential backoff schedule keyed by attempt count (§8.2): after attempt N (1-based) the next attempt
    /// is scheduled now + BackoffSchedule[N-1], capped at the last entry. ~1m, 5m, 30m, 2h, 6h.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> BackoffSchedule = new[]
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6)
    };
}
