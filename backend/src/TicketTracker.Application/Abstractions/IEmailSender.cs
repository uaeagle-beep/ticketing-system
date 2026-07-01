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
}
