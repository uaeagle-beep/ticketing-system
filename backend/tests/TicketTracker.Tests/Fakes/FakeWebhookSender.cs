using System.Collections.Concurrent;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IWebhookSender"/> (Wave 3, ADR-0021, §8.4). Captures every send (url, body,
/// headers) so tests can assert the HMAC signature over the raw body + the delivery headers, and returns a
/// SCRIPTED outcome so the delivery drain runs deterministically: by default HTTP 200 (delivered), or a
/// per-test status / exception to exercise retry/backoff/max-attempts. Registered as a singleton in the test
/// factory so captures + the script survive across the scoped lifetimes of a drain.
/// </summary>
public sealed class FakeWebhookSender : IWebhookSender
{
    private readonly ConcurrentQueue<Sent> _sent = new();

    /// <summary>Set the status every subsequent send returns (2xx = delivered). Default 200.</summary>
    public int NextStatusCode { get; set; } = 200;

    /// <summary>When set, every send throws this (simulates a transport failure / timeout path).</summary>
    public Func<Exception>? ThrowOnSend { get; set; }

    /// <summary>When set, overrides <see cref="NextStatusCode"/> to return a not-Success result with this error.</summary>
    public string? FailWithError { get; set; }

    /// <summary>Every captured send, in order.</summary>
    public IReadOnlyList<Sent> Sends => _sent.ToArray();

    /// <summary>The most recent captured send (or null).</summary>
    public Sent? Last => _sent.LastOrDefault();

    public Task<WebhookSendResult> SendAsync(
        string url,
        string body,
        IReadOnlyDictionary<string, string> headers,
        TimeSpan timeout,
        CancellationToken ct)
    {
        _sent.Enqueue(new Sent(url, body, new Dictionary<string, string>(headers)));

        if (ThrowOnSend is not null)
            throw ThrowOnSend();

        if (FailWithError is not null)
            return Task.FromResult(new WebhookSendResult(false, null, FailWithError));

        var success = NextStatusCode is >= 200 and < 300;
        return Task.FromResult(new WebhookSendResult(success, NextStatusCode,
            success ? null : $"HTTP {NextStatusCode}"));
    }

    /// <summary>A captured send: the target url, the exact raw body, and the delivery headers.</summary>
    public sealed record Sent(string Url, string Body, IReadOnlyDictionary<string, string> Headers);
}
