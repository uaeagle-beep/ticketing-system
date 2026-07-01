using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Infrastructure.Email;

/// <summary>
/// Production email sender using MailKit (ADR-0004). Builds the verification message and
/// sends it through the configured SMTP relay (default relay1.dataart.com). STARTTLS is used
/// when configured; credentials are supplied only when present (relay may be open). This class
/// only transports the message — the resilience policy (do not roll back signup on failure)
/// lives in <c>AuthService</c>.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public Task SendVerificationEmailAsync(string toEmail, string verificationLink, CancellationToken ct)
        => SendAsync(
            toEmail,
            subject: "Verify your email address",
            textBody:
                "Welcome to Ticket Tracker.\r\n\r\n" +
                "Please verify your email address by opening the link below (valid for 24 hours):\r\n" +
                verificationLink + "\r\n\r\n" +
                "If you did not create an account, you can ignore this message.",
            htmlBody:
                "<p>Welcome to Ticket Tracker.</p>" +
                "<p>Please verify your email address by clicking the link below (valid for 24 hours):</p>" +
                $"<p><a href=\"{verificationLink}\">Verify my email</a></p>" +
                "<p>If you did not create an account, you can ignore this message.</p>",
            ct);

    public Task SendPasswordResetEmailAsync(string toEmail, string resetLink, CancellationToken ct)
        => SendAsync(
            toEmail,
            subject: "Reset your password",
            textBody:
                "We received a request to reset your Ticket Tracker password.\r\n\r\n" +
                "Open the link below to choose a new password (valid for 1 hour):\r\n" +
                resetLink + "\r\n\r\n" +
                "If you did not request a password reset, you can safely ignore this message.",
            htmlBody:
                "<p>We received a request to reset your Ticket Tracker password.</p>" +
                "<p>Click the link below to choose a new password (valid for 1 hour):</p>" +
                $"<p><a href=\"{resetLink}\">Reset my password</a></p>" +
                "<p>If you did not request a password reset, you can safely ignore this message.</p>",
            ct);

    public Task SendNotificationDigestEmailAsync(string toEmail, IReadOnlyList<string> lines, string deepLinkBase, CancellationToken ct)
    {
        var count = lines.Count;
        var notificationsUrl = $"{deepLinkBase.TrimEnd('/')}/notifications";
        var textLines = string.Join("\r\n", lines.Select(l => $"- {l}"));
        var htmlLines = string.Join(string.Empty, lines.Select(l => $"<li>{System.Net.WebUtility.HtmlEncode(l)}</li>"));

        return SendAsync(
            toEmail,
            subject: count == 1
                ? "You have a new update on Ticket Tracker"
                : $"You have {count} new updates on Ticket Tracker",
            textBody:
                "You have new activity on tickets you follow:\r\n\r\n" +
                textLines + "\r\n\r\n" +
                "View your notifications:\r\n" + notificationsUrl + "\r\n\r\n" +
                "You can turn off these emails in your account settings.",
            htmlBody:
                "<p>You have new activity on tickets you follow:</p>" +
                $"<ul>{htmlLines}</ul>" +
                $"<p><a href=\"{notificationsUrl}\">View your notifications</a></p>" +
                "<p>You can turn off these emails in your account settings.</p>",
            ct);
    }

    private async Task SendAsync(string toEmail, string subject, string textBody, string htmlBody, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { TextBody = textBody, HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();

        var socketOptions = _options.UseStartTls
            ? SecureSocketOptions.StartTlsWhenAvailable
            : SecureSocketOptions.Auto;

        await client.ConnectAsync(_options.Host, _options.Port, socketOptions, ct);

        if (!string.IsNullOrEmpty(_options.Username))
            await client.AuthenticateAsync(_options.Username, _options.Password, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
