namespace TicketTracker.Application.Abstractions;

/// <summary>
/// Email delivery port (ADR-0004). Production binds an SMTP/MailKit implementation;
/// tests bind a fake that records sent messages. A transient send failure must NOT roll
/// back account creation (the user can use resend) — callers handle that policy.
/// </summary>
public interface IEmailSender
{
    Task SendVerificationEmailAsync(string toEmail, string verificationLink, CancellationToken ct);

    /// <summary>
    /// Sends a password-reset link (F-01, ADR-0010). A separate method from verification because the
    /// link path and copy differ. Like verification, a transient send failure must NOT roll back the
    /// token issuance — the caller swallows it with a warning (never logging the token).
    /// </summary>
    Task SendPasswordResetEmailAsync(string toEmail, string resetLink, CancellationToken ct);

    /// <summary>
    /// Sends ONE coalesced notification digest to a recipient (Wave 2, ADR-0014). Called by the email
    /// outbox worker with all of the recipient's pending notification <paramref name="lines"/> (already
    /// rendered summaries), so a burst of events within the debounce window becomes a single email.
    /// <paramref name="deepLinkBase"/> is the SPA base URL used to build a "view notifications" link.
    /// A send failure surfaces to the dispatcher, which isolates the failing recipient (per-recipient
    /// try/catch) and retries their rows next tick.
    /// </summary>
    Task SendNotificationDigestEmailAsync(string toEmail, IReadOnlyList<string> lines, string deepLinkBase, CancellationToken ct);
}
