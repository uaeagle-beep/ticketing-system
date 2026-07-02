using Microsoft.Extensions.Logging;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Infrastructure.Email;

/// <summary>
/// Dev-only email sender (selected by EMAIL_SENDER=log). Instead of contacting an SMTP relay it
/// writes the verification link to the application log, so a local run without a reachable SMTP
/// server can still complete the signup -> verify -> login flow. Not used unless explicitly
/// opted in via configuration; Production keeps the real <see cref="SmtpEmailSender"/>.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendVerificationEmailAsync(string toEmail, string verificationLink, CancellationToken ct)
    {
        _logger.LogInformation("[DEV EMAIL] Verification link for {Email}: {Link}", toEmail, verificationLink);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string resetLink, CancellationToken ct)
    {
        _logger.LogInformation("[DEV EMAIL] Password reset link for {Email}: {Link}", toEmail, resetLink);
        return Task.CompletedTask;
    }

    public Task SendNotificationDigestEmailAsync(string toEmail, IReadOnlyList<string> lines, string deepLinkBase, CancellationToken ct)
    {
        _logger.LogInformation("[DEV EMAIL] Notification digest for {Email} ({Count} line(s)): {Lines}",
            toEmail, lines.Count, string.Join(" | ", lines));
        return Task.CompletedTask;
    }
}
