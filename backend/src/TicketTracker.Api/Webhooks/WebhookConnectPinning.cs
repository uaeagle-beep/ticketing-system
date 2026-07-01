using System.Net;
using System.Net.Sockets;
using TicketTracker.Infrastructure.Webhooks;

namespace TicketTracker.Api.Webhooks;

/// <summary>
/// Connect-time SSRF pinning for the outbound webhooks <see cref="HttpClient"/> (SEC-2, ADR-0021 §7.4,
/// CWE-918). The dispatcher's send-time <c>WebhookUrlValidator.IsAllowedAtSendTimeAsync</c> resolves + validates
/// the host, but <see cref="HttpClient"/> then re-resolves DNS independently at connect — a low-TTL hostile
/// name can pass the pre-check yet connect to a private IP (DNS-rebinding, the residual §7.4 risk). This
/// <see cref="SocketsHttpHandler.ConnectCallback"/> closes that window: it resolves the host itself, re-checks
/// the ACTUALLY-resolved address against the SAME block-list (<see cref="WebhookUrlValidator.IsBlockedAddress"/>
/// — one shared classifier, no duplicate list), and connects ONLY to a non-blocked address, aborting otherwise.
///
/// It returns a raw TCP <see cref="NetworkStream"/>, so <see cref="SocketsHttpHandler"/> layers TLS itself
/// using the request URI's host → Host header and TLS SNI are preserved (we do not intercept the handshake).
/// The handler keeps <c>AllowAutoRedirect=false</c>, so a subscriber can never 3xx-bounce to an internal
/// target either. Wired ONLY when <c>WEBHOOKS_ALLOW_INSECURE</c> is off (prod); the escape hatch omits the
/// callback so tests/dev can target localhost.
/// </summary>
public static class WebhookConnectPinning
{
    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endPoint = context.DnsEndPoint;

        // Resolve the host ourselves so we validate the SAME addresses we connect to. A literal IP is used
        // directly; a hostname is resolved now (its resolution here is what we pin against — no re-resolve).
        IPAddress[] addresses;
        if (IPAddress.TryParse(endPoint.Host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            addresses = await Dns.GetHostAddressesAsync(endPoint.Host, ct).ConfigureAwait(false);
            if (addresses.Length == 0)
                throw new HttpRequestException($"Webhook host '{endPoint.Host}' did not resolve to any address.");
        }

        // If ANY resolved address is blocked, refuse the whole connection (belt-and-suspenders, mirrors the
        // send-time pre-check): a hostile name resolving to a mix of public + private must not connect at all.
        foreach (var address in addresses)
        {
            if (WebhookUrlValidator.IsBlockedAddress(address))
                throw new HttpRequestException(
                    $"Webhook target '{endPoint.Host}' resolved to a blocked address ({address}); refusing to connect (SSRF).");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            // Connect to the exact validated addresses — no further DNS resolution happens inside the handler.
            await socket.ConnectAsync(addresses, endPoint.Port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
