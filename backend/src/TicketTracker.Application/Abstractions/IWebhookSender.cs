namespace TicketTracker.Application.Abstractions;

/// <summary>
/// The outbound-HTTP seam for webhook delivery (Wave 3, ADR-0021, §8.1). Production wraps
/// <c>IHttpClientFactory</c> (a named client with <c>AllowAutoRedirect=false</c> + the per-attempt timeout);
/// tests bind a fake returning scripted statuses so the delivery drain runs deterministically over
/// in-memory SQLite with no real sockets (ASR-W3-5). Keeping ALL correctness (retry/backoff/status) in
/// <c>WebhookDeliveryDispatcher.DrainOnceAsync</c> and only the transport behind this seam mirrors the
/// email worker's fake-<c>IEmailSender</c> split.
/// </summary>
public interface IWebhookSender
{
    /// <summary>
    /// POST the signed body to <paramref name="url"/> with the given headers, honoring
    /// <paramref name="timeout"/> and never following redirects. Returns the delivery outcome (never throws
    /// for an HTTP error status — a 4xx/5xx is a <see cref="WebhookSendResult"/> with <c>Success=false</c>;
    /// a transport failure / timeout is surfaced via <c>Success=false</c> + <c>Error</c>, or may throw for
    /// the dispatcher's per-delivery try/catch to record).
    /// </summary>
    Task<WebhookSendResult> SendAsync(
        string url,
        string body,
        IReadOnlyDictionary<string, string> headers,
        TimeSpan timeout,
        CancellationToken ct);
}

/// <summary>
/// The outcome of a single webhook send attempt. <see cref="StatusCode"/> is null when the endpoint was
/// never reached (DNS/connect/timeout); <see cref="Error"/> carries a short reason for the audit row.
/// </summary>
public sealed record WebhookSendResult(bool Success, int? StatusCode, string? Error);
