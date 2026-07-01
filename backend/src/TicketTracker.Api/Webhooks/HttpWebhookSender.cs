using TicketTracker.Application.Abstractions;

namespace TicketTracker.Api.Webhooks;

/// <summary>
/// Production <see cref="IWebhookSender"/> over <see cref="IHttpClientFactory"/> (Wave 3, ADR-0021, §9.3).
/// Uses a named client configured (in Program.cs) with <c>AllowAutoRedirect=false</c> so a subscriber can
/// never 3xx-bounce the request to an internal target (SSRF, §7.4). Applies the per-attempt timeout via a
/// linked <see cref="CancellationTokenSource"/>, reads/discards a small bounded response body, and reports a
/// non-2xx as a failed result (never throws for an HTTP status). Transport failures / timeouts surface as
/// <c>Success=false</c> with a short <c>Error</c> for the audit row — the dispatcher owns retry/backoff.
/// This is the thin transport shell; ALL delivery correctness lives in <c>WebhookDeliveryDispatcher</c>.
/// </summary>
public sealed class HttpWebhookSender : IWebhookSender
{
    /// <summary>The named HttpClient (registered in Program.cs with AllowAutoRedirect=false).</summary>
    public const string HttpClientName = "webhooks";

    private const int MaxResponseBytes = 4 * 1024; // we only care about the status; cap the drained body

    private readonly IHttpClientFactory _factory;
    private readonly ILogger<HttpWebhookSender> _logger;

    public HttpWebhookSender(IHttpClientFactory factory, ILogger<HttpWebhookSender> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<WebhookSendResult> SendAsync(
        string url,
        string body,
        IReadOnlyDictionary<string, string> headers,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        try
        {
            var client = _factory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
            foreach (var (name, value) in headers)
                request.Headers.TryAddWithoutValidation(name, value);

            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, linked.Token);

            // Drain a small bounded prefix of the body then discard (we only care about the status).
            await DrainBoundedAsync(response, linked.Token);

            var code = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? new WebhookSendResult(true, code, null)
                : new WebhookSendResult(false, code, $"HTTP {code}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutdown / caller cancellation — bubble so the drain stops cleanly.
            throw;
        }
        catch (OperationCanceledException)
        {
            // The per-attempt timeout fired (linked token) — a delivery timeout, not a shutdown.
            return new WebhookSendResult(false, null, "timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Webhook send transport error.");
            return new WebhookSendResult(false, null, Shorten(ex.Message));
        }
    }

    private static async Task DrainBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[MaxResponseBytes];
            _ = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
        }
        catch
        {
            // Ignore body-read failures — the status is what matters.
        }
    }

    private static string Shorten(string message)
        => message.Length <= 200 ? message : message[..200];
}
