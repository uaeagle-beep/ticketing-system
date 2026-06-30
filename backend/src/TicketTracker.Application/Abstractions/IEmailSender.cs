namespace TicketTracker.Application.Abstractions;

/// <summary>
/// Email delivery port (ADR-0004). Production binds an SMTP/MailKit implementation;
/// tests bind a fake that records sent messages. A transient send failure must NOT roll
/// back account creation (the user can use resend) — callers handle that policy.
/// </summary>
public interface IEmailSender
{
    Task SendVerificationEmailAsync(string toEmail, string verificationLink, CancellationToken ct);
}
