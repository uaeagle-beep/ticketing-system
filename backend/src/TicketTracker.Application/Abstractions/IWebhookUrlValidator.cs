namespace TicketTracker.Application.Abstractions;

/// <summary>
/// SSRF policy for webhook target URLs (Wave 3, ADR-0021, §7.4, [ASSUMPTION W3-WH-SSRF]). Two checks:
/// <list type="bullet">
/// <item><see cref="ValidateForSubscribe"/> — a cheap syntactic gate at create/update time: the URL must be
/// absolute and <c>https://</c> (http allowed only when <c>WEBHOOKS_ALLOW_INSECURE=true</c> for local dev).</item>
/// <item><see cref="IsAllowedAtSendTimeAsync"/> — resolves the host at DELIVERY time and rejects
/// private/loopback/link-local/ULA/metadata addresses. Re-resolving at send (not only at subscribe) defeats
/// DNS-rebinding.</item>
/// </list>
/// Both are relaxed when the insecure escape hatch is on so tests/local can target http/localhost.
/// </summary>
public interface IWebhookUrlValidator
{
    /// <summary>
    /// True when the URL is a syntactically valid subscribe target (absolute + https, or http/anything when
    /// the insecure escape hatch is on). Does NOT resolve DNS — that is the send-time check.
    /// </summary>
    bool ValidateForSubscribe(string? url, out string? error);

    /// <summary>
    /// Resolve <paramref name="url"/>'s host and return false if it maps to a blocked
    /// (private/loopback/link-local/ULA/metadata) address — the anti-DNS-rebind send-time gate. When the
    /// insecure escape hatch is on, always returns true (tests/local target localhost).
    /// </summary>
    Task<bool> IsAllowedAtSendTimeAsync(string url, CancellationToken ct);
}
