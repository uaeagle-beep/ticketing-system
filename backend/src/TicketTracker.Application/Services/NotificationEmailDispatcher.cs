using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Options;
using TicketTracker.Domain.Entities;

namespace TicketTracker.Application.Services;

/// <summary>
/// The email outbox drain (Wave 2, ADR-0014). Holds ALL correctness for coalesced notification email;
/// the hosted <c>NotificationEmailWorker</c> is a thin timer over <see cref="DrainOnceAsync"/>. Takes
/// <c>now</c> as a parameter (never reads wall-clock) so tests drive it deterministically with a fake
/// clock + fake sender. The whole cycle runs inside the Npgsql-retry-safe execution-strategy transaction
/// pattern (also correct under SQLite's non-retrying default strategy — §ASR-W2-3).
/// </summary>
public sealed class NotificationEmailDispatcher
{
    private readonly IAppDbContext _db;
    private readonly IEmailSender _email;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationEmailDispatcher> _logger;

    public NotificationEmailDispatcher(
        IAppDbContext db,
        IEmailSender email,
        IOptions<NotificationOptions> options,
        ILogger<NotificationEmailDispatcher> logger)
    {
        _db = db;
        _email = email;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Drain the outbox once: select un-emailed notifications older than the debounce window, group by
    /// recipient, send ONE combined digest per eligible recipient, and stamp <c>emailed_at = now</c> on
    /// every processed row (idempotency key). Email-off / blocked recipients are marked emailed WITHOUT
    /// sending (so they never backlog). Per-recipient try/catch isolates a failing recipient (their rows
    /// stay null and retry next tick). Returns the number of recipients actually emailed (for metrics).
    /// </summary>
    public async Task<int> DrainOnceAsync(DateTime now, CancellationToken ct)
    {
        var emailedRecipients = 0;
        var cutoff = now - TimeSpan.FromSeconds(_options.EmailDebounceSeconds);

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            emailedRecipients = 0; // reset on each execution-strategy attempt (retry-safety)
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // Eligible outbox rows: never emailed and older than the debounce window (bounded, small scale).
            var pending = await _db.Notifications
                .Where(n => n.EmailedAt == null && n.CreatedAt <= cutoff)
                .OrderBy(n => n.RecipientId).ThenBy(n => n.CreatedAt)
                .ToListAsync(ct);

            if (pending.Count == 0)
            {
                await tx.CommitAsync(ct);
                return;
            }

            foreach (var group in pending.GroupBy(n => n.RecipientId))
            {
                var recipient = await _db.Users.FindAsync(new object[] { group.Key }, ct);

                // Skip blocked / email-off recipients: mark emailed (no send) so they never backlog.
                var emailOff = recipient is null || recipient.IsBlocked || !recipient.EmailNotificationsEnabled;

                if (emailOff)
                {
                    StampEmailed(group, now);
                    continue;
                }

                var lines = group.Select(n => n.Summary).ToList();
                try
                {
                    await _email.SendNotificationDigestEmailAsync(recipient!.Email, lines, _options.FrontendUrl, ct);
                    StampEmailed(group, now);
                    emailedRecipients++;
                }
                catch (Exception ex)
                {
                    // Isolate a bad recipient: leave their rows null (retried next tick), do not stamp,
                    // do not fail the whole drain (R-4). Never log the email address at error level with PII risk.
                    _logger.LogError(ex,
                        "Failed to send notification digest to a recipient; leaving {Count} row(s) for retry.",
                        group.Count());
                }
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return emailedRecipients;
    }

    private static void StampEmailed(IEnumerable<Notification> group, DateTime now)
    {
        foreach (var n in group)
            n.EmailedAt = now; // idempotency: emailed_at IS NULL is the only selector, so never re-sent
    }
}
