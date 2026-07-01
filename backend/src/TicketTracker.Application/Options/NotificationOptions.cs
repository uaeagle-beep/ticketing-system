namespace TicketTracker.Application.Options;

/// <summary>
/// Email-notification outbox tuning (Wave 2, ADR-0014 / §7.4). Bound from environment in Program.cs.
/// All three are env-tunable so the PO can dial noise vs latency without a code change.
/// </summary>
public sealed class NotificationOptions
{
    /// <summary>Worker tick interval in seconds (env NOTIFICATION_WORKER_POLL_SECONDS, default 15).</summary>
    public int WorkerPollSeconds { get; set; } = 15;

    /// <summary>
    /// Coalescing window in seconds (env NOTIFICATION_EMAIL_DEBOUNCE_SECONDS, default 60): the minimum
    /// age before a notification is eligible to be emailed, so a rapid burst coalesces into one digest.
    /// </summary>
    public int EmailDebounceSeconds { get; set; } = 60;

    /// <summary>
    /// Master kill-switch for the worker (env NOTIFICATIONS_EMAIL_ENABLED, default true). When false the
    /// hosted worker returns immediately; in-app notifications are unaffected.
    /// </summary>
    public bool EmailEnabled { get; set; } = true;

    /// <summary>Base URL used to build the "view notifications" deep link in the digest email.</summary>
    public string FrontendUrl { get; set; } = "http://localhost:8080";
}
