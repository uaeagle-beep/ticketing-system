using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Options;

namespace TicketTracker.Infrastructure.Webhooks;

/// <summary>
/// SSRF policy enforcement for webhook URLs (Wave 3, ADR-0021, §7.4, [ASSUMPTION W3-WH-SSRF]).
/// <list type="bullet">
/// <item>Subscribe-time: URL must be absolute + <c>https://</c> (http only when the insecure escape hatch is on).</item>
/// <item>Send-time: the host is re-resolved and REJECTED if it maps to a private/loopback/link-local/ULA/
/// metadata address — this defeats DNS-rebinding (a name that resolved public at subscribe could resolve
/// internal at send). Any literal IP is checked directly.</item>
/// </list>
/// When <c>WEBHOOKS_ALLOW_INSECURE=true</c> both checks relax so tests/local can target http/localhost.
/// </summary>
public sealed class WebhookUrlValidator : IWebhookUrlValidator
{
    private readonly WebhookOptions _options;

    public WebhookUrlValidator(IOptions<WebhookOptions> options) => _options = options.Value;

    public bool ValidateForSubscribe(string? url, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            error = "A webhook URL is required.";
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            error = "The webhook URL is not a valid absolute URL.";
            return false;
        }

        // https-only unless the insecure escape hatch is on (http permitted for local dev/tests).
        var schemeOk = uri.Scheme == Uri.UriSchemeHttps
                       || (_options.AllowInsecure && uri.Scheme == Uri.UriSchemeHttp);
        if (!schemeOk)
        {
            error = "The webhook URL must use https.";
            return false;
        }

        return true;
    }

    public async Task<bool> IsAllowedAtSendTimeAsync(string url, CancellationToken ct)
    {
        // Local/test escape hatch: never block (tests target localhost / use a fake sender).
        if (_options.AllowInsecure)
            return true;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // A URL literal IP is checked directly; a hostname is resolved NOW (anti-DNS-rebind).
        IEnumerable<IPAddress> addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try
            {
                var resolved = await Dns.GetHostAddressesAsync(uri.Host, ct);
                if (resolved.Length == 0)
                    return false;
                addresses = resolved;
            }
            catch (SocketException)
            {
                return false; // unresolvable → treat as not-allowed (delivery fails cleanly)
            }
        }

        // A single blocked address in the set fails the whole check (belt-and-suspenders).
        foreach (var addr in addresses)
            if (IsBlocked(addr))
                return false;

        return true;
    }

    /// <summary>
    /// True when the address is one we must never call: loopback, link-local (incl. cloud metadata
    /// 169.254.169.254), private (10/8, 172.16/12, 192.168/16), unspecified, IPv6 loopback (::1), or ULA
    /// (fc00::/7). IPv4-mapped IPv6 is unwrapped and re-checked.
    /// </summary>
    private static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            // 0.0.0.0/8 unspecified/this-network
            if (b[0] == 0) return true;
            // 10.0.0.0/8
            if (b[0] == 10) return true;
            // 127.0.0.0/8 (covered by IsLoopback, kept explicit)
            if (b[0] == 127) return true;
            // 169.254.0.0/16 link-local incl. 169.254.169.254 cloud metadata
            if (b[0] == 169 && b[1] == 254) return true;
            // 172.16.0.0/12
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            // 192.168.0.0/16
            if (b[0] == 192 && b[1] == 168) return true;
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal) return true; // fe80::/10
            if (IPAddress.IPv6Any.Equals(address)) return true; // ::
            var b = address.GetAddressBytes();
            // ULA fc00::/7 (first 7 bits 1111 110x → first byte 0xFC or 0xFD)
            if ((b[0] & 0xFE) == 0xFC) return true;
            return false;
        }

        // Unknown families are blocked conservatively.
        return true;
    }
}
