using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Options;
using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Services;

/// <summary>
/// The webhook delivery outbox drain (Wave 3, ADR-0021, §8) — the exact mirror of
/// <see cref="NotificationEmailDispatcher"/>. Holds ALL delivery correctness (select due pending rows, SSRF
/// re-check at send, HMAC-SHA256 sign the raw body, POST via <see cref="IWebhookSender"/> with a 10s timeout
/// and no redirects, update status/attempts/next_attempt_at with exponential backoff, max attempts → failed).
/// Takes <c>now</c> as a parameter (never reads wall-clock) so tests drive it deterministically with a fake
/// clock + fake sender. The whole cycle runs inside the Npgsql-retry-safe execution-strategy transaction
/// (also correct under SQLite). Per-delivery try/catch isolates a dead endpoint (mirrors the email
/// per-recipient try/catch). The thin <c>WebhookDeliveryWorker</c> is a timer over <see cref="DrainOnceAsync"/>.
/// </summary>
public sealed class WebhookDeliveryDispatcher
{
    /// <summary>Header names for the signed delivery ([ASSUMPTION W3-WH-SIGNATURE]).</summary>
    public const string SignatureHeader = "X-TicketTracker-Signature";
    public const string EventHeader = "X-TicketTracker-Event";
    public const string DeliveryHeader = "X-TicketTracker-Delivery";
    public const string TimestampHeader = "X-TicketTracker-Timestamp";

    private readonly IAppDbContext _db;
    private readonly IWebhookSender _sender;
    private readonly ISecretProtector _secrets;
    private readonly IWebhookUrlValidator _ssrf;
    private readonly WebhookOptions _options;
    private readonly ILogger<WebhookDeliveryDispatcher> _logger;

    public WebhookDeliveryDispatcher(
        IAppDbContext db,
        IWebhookSender sender,
        ISecretProtector secrets,
        IWebhookUrlValidator ssrf,
        IOptions<WebhookOptions> options,
        ILogger<WebhookDeliveryDispatcher> logger)
    {
        _db = db;
        _sender = sender;
        _secrets = secrets;
        _ssrf = ssrf;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Drain due deliveries once: select <c>status='pending' AND next_attempt_at &lt;= now</c> (bounded, oldest
    /// first), sign + send each (per-delivery try/catch), and update status/attempts/next_attempt_at with
    /// backoff (max attempts → failed). Takes <c>now</c> as a parameter so tests drive it deterministically.
    /// Returns the number of deliveries attempted (for metrics). Runs inside
    /// <c>CreateExecutionStrategy().ExecuteAsync</c> + <c>BeginTransactionAsync</c> — exactly like the email drain.
    /// </summary>
    public async Task<int> DrainOnceAsync(DateTime now, CancellationToken ct)
    {
        var attempted = 0;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            attempted = 0; // reset on each execution-strategy attempt (retry-safety)
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // Due, pending rows — bounded batch, oldest-scheduled first (the outbox selector, §8.2).
            var due = await _db.WebhookDeliveries
                .Where(d => d.Status == WebhookDeliveryStatusCanonical.Pending
                            && d.NextAttemptAt != null
                            && d.NextAttemptAt <= now)
                .OrderBy(d => d.NextAttemptAt)
                .ThenBy(d => d.CreatedAt)
                .Take(_options.BatchSize)
                .ToListAsync(ct);

            if (due.Count == 0)
            {
                await tx.CommitAsync(ct);
                return;
            }

            // Load the subscriptions referenced by this batch in one round-trip.
            var subIds = due.Select(d => d.SubscriptionId).Distinct().ToList();
            var subs = await _db.WebhookSubscriptions
                .Where(s => subIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);

            foreach (var delivery in due)
            {
                attempted++;
                try
                {
                    await ProcessOneAsync(delivery, subs, now, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // host shutdown — stop the drain, leave the row pending for next tick
                }
                catch (Exception ex)
                {
                    // Isolate a bad delivery: record the failure + schedule/fail, never abort the whole drain (R-4).
                    _logger.LogError(ex, "Webhook delivery {DeliveryId} threw; scheduling retry-or-fail.", delivery.Id);
                    RecordFailure(delivery, statusCode: null, error: Shorten(ex.Message), now);
                }
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return attempted;
    }

    private async Task ProcessOneAsync(
        WebhookDelivery delivery,
        IReadOnlyDictionary<Guid, WebhookSubscription> subs,
        DateTime now,
        CancellationToken ct)
    {
        // Subscription gone / deactivated → terminal fail (do not send; keep the row for audit).
        if (!subs.TryGetValue(delivery.SubscriptionId, out var sub) || !sub.Active)
        {
            delivery.Status = WebhookDeliveryStatusCanonical.Failed;
            delivery.NextAttemptAt = null;
            delivery.LastError = "subscription_inactive_or_deleted";
            return;
        }

        // SSRF re-check AT SEND TIME (anti-DNS-rebind, §7.4): a blocked host is a failed attempt.
        if (!await _ssrf.IsAllowedAtSendTimeAsync(sub.Url, ct))
        {
            RecordFailure(delivery, statusCode: null, error: "ssrf_blocked", now);
            return;
        }

        var secret = _secrets.Unprotect(sub.SecretEncrypted);
        var body = delivery.PayloadJson;
        var signature = "sha256=" + HmacSha256Hex(secret, body);
        var timestamp = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeSeconds();

        var headers = new Dictionary<string, string>
        {
            [SignatureHeader] = signature,
            [EventHeader] = delivery.EventType,
            [DeliveryHeader] = delivery.Id.ToString(),
            [TimestampHeader] = timestamp.ToString()
        };

        var result = await _sender.SendAsync(
            sub.Url, body, headers, TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)), ct);

        if (result.Success)
        {
            delivery.Status = WebhookDeliveryStatusCanonical.Delivered;
            delivery.DeliveredAt = now;
            delivery.NextAttemptAt = null;
            delivery.Attempts += 1;
            delivery.LastStatusCode = result.StatusCode;
            delivery.LastError = null;
        }
        else
        {
            RecordFailure(delivery, result.StatusCode, result.Error, now);
        }
    }

    /// <summary>
    /// Record a failed attempt: increment attempts, capture the status/error, and either schedule the next
    /// attempt via the backoff schedule or, once the attempt budget is exhausted, mark the row failed
    /// (terminal; next_attempt_at cleared). Mirrors the "schedule-or-fail" step in §8.2.
    /// </summary>
    private void RecordFailure(WebhookDelivery delivery, int? statusCode, string? error, DateTime now)
    {
        delivery.Attempts += 1;
        delivery.LastStatusCode = statusCode;
        delivery.LastError = Shorten(error) ?? "delivery_failed";

        if (delivery.Attempts >= Math.Max(1, _options.MaxAttempts))
        {
            delivery.Status = WebhookDeliveryStatusCanonical.Failed;
            delivery.NextAttemptAt = null;
        }
        else
        {
            delivery.Status = WebhookDeliveryStatusCanonical.Pending;
            delivery.NextAttemptAt = now + BackoffFor(delivery.Attempts);
        }
    }

    /// <summary>Backoff for the just-completed attempt (1-based); capped at the last schedule entry (§8.2).</summary>
    private static TimeSpan BackoffFor(int attempts)
    {
        var schedule = WebhookOptions.BackoffSchedule;
        var idx = Math.Min(attempts - 1, schedule.Count - 1);
        if (idx < 0) idx = 0;
        return schedule[idx];
    }

    private static string HmacSha256Hex(string secret, string body)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(body);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexStringLower(hash);
    }

    private static string? Shorten(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= 500 ? value : value[..500];
    }
}
