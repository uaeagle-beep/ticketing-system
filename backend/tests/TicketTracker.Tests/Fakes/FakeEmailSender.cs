using System.Collections.Concurrent;
using System.Web;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IEmailSender"/> (ADR-0004). Records every (to, link) pair the
/// API would have emailed so the verify-flow tests can extract the raw token from the captured
/// link (<c>{FRONTEND_URL}/verify-email?token=RAW</c>) and complete verification offline — no SMTP.
/// Registered as a singleton so captures survive across the scoped service lifetimes of a request.
/// </summary>
public sealed class FakeEmailSender : IEmailSender
{
    public sealed record SentEmail(string To, string Link);

    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyList<SentEmail> Sent => _sent.ToArray();

    public Task SendVerificationEmailAsync(string toEmail, string verificationLink, CancellationToken ct)
    {
        _sent.Enqueue(new SentEmail(toEmail, verificationLink));
        return Task.CompletedTask;
    }

    /// <summary>The most recent email captured for the given address (case-insensitive), or null.</summary>
    public SentEmail? LastFor(string email)
        => _sent.Reverse()
            .FirstOrDefault(e => string.Equals(e.To, email, StringComparison.OrdinalIgnoreCase));

    /// <summary>Extract the raw <c>token</c> query-string value from a captured verification link.</summary>
    public static string ExtractToken(string verificationLink)
    {
        var uri = new Uri(verificationLink);
        var token = HttpUtility.ParseQueryString(uri.Query).Get("token");
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException($"No token found in verification link '{verificationLink}'.");
        return token;
    }

    public void Clear()
    {
        while (_sent.TryDequeue(out _)) { }
    }
}
