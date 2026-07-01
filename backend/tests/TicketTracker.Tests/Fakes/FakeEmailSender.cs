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
    public enum EmailKind { Verification, PasswordReset, NotificationDigest }

    public sealed record SentEmail(string To, string Link, EmailKind Kind = EmailKind.Verification);

    /// <summary>A captured notification digest (Wave 2, ADR-0014): recipient + the coalesced summary lines.</summary>
    public sealed record SentDigest(string To, IReadOnlyList<string> Lines, string DeepLinkBase);

    private readonly ConcurrentQueue<SentEmail> _sent = new();
    private readonly ConcurrentQueue<SentDigest> _digests = new();

    /// <summary>
    /// Addresses (case-insensitive) whose digest send should THROW, to exercise the dispatcher's
    /// per-recipient try/catch isolation (R-4: one bad recipient must not starve the others). Opt-in and
    /// empty by default, so existing tests are unaffected. Set via <c>Factory.Email.FailDigestsFor.Add(email)</c>.
    /// </summary>
    public HashSet<string> FailDigestsFor { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SentEmail> Sent => _sent.ToArray();

    /// <summary>Every notification digest captured (tests assert coalescing / idempotency).</summary>
    public IReadOnlyList<SentDigest> Digests => _digests.ToArray();

    public Task SendVerificationEmailAsync(string toEmail, string verificationLink, CancellationToken ct)
    {
        _sent.Enqueue(new SentEmail(toEmail, verificationLink, EmailKind.Verification));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string resetLink, CancellationToken ct)
    {
        _sent.Enqueue(new SentEmail(toEmail, resetLink, EmailKind.PasswordReset));
        return Task.CompletedTask;
    }

    public Task SendNotificationDigestEmailAsync(string toEmail, IReadOnlyList<string> lines, string deepLinkBase, CancellationToken ct)
    {
        if (FailDigestsFor.Contains(toEmail))
            throw new InvalidOperationException($"Simulated digest send failure for '{toEmail}'.");
        _digests.Enqueue(new SentDigest(toEmail, lines.ToArray(), deepLinkBase));
        return Task.CompletedTask;
    }

    /// <summary>All digests captured for the given address (case-insensitive), newest last.</summary>
    public IReadOnlyList<SentDigest> DigestsFor(string email)
        => _digests.Where(d => string.Equals(d.To, email, StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>The most recent email captured for the given address (case-insensitive), or null.</summary>
    public SentEmail? LastFor(string email)
        => _sent.Reverse()
            .FirstOrDefault(e => string.Equals(e.To, email, StringComparison.OrdinalIgnoreCase));

    /// <summary>The most recent email of the given kind captured for the address (case-insensitive), or null.</summary>
    public SentEmail? LastFor(string email, EmailKind kind)
        => _sent.Reverse()
            .FirstOrDefault(e => e.Kind == kind && string.Equals(e.To, email, StringComparison.OrdinalIgnoreCase));

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
