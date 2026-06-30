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

    public async Task SendVerificationEmailAsync(string toEmail, string verificationLink, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = "Verify your email address";

        var bodyBuilder = new BodyBuilder
        {
            TextBody =
                "Welcome to Ticket Tracker.\r\n\r\n" +
                "Please verify your email address by opening the link below (valid for 24 hours):\r\n" +
                verificationLink + "\r\n\r\n" +
                "If you did not create an account, you can ignore this message.",
            HtmlBody =
                "<p>Welcome to Ticket Tracker.</p>" +
                "<p>Please verify your email address by clicking the link below (valid for 24 hours):</p>" +
                $"<p><a href=\"{verificationLink}\">Verify my email</a></p>" +
                "<p>If you did not create an account, you can ignore this message.</p>"
        };
        message.Body = bodyBuilder.ToMessageBody();

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
