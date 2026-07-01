using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Services;

/// <summary>
/// Team-scoped webhook subscription management (Wave 3, ADR-0021, §5.5). Every method is M(team of the
/// subscription): resolve the team/subscription (404 if absent) then <c>RequireTeamAccess</c> (403) —
/// resolve-then-check ordering (anti-IDOR, §3.3). URLs are validated against the SSRF subscribe-time policy
/// (400 keyed <c>url</c>); subscribed event types must each be a canonical <see cref="EventType"/> code or
/// <c>"*"</c> (400 keyed <c>eventTypes</c>). The signing secret is generated + AES-GCM-encrypted via
/// <see cref="ISecretProtector"/> and returned ONCE on create/rotate (never serialized back). Create/update
/// run inside the execution-strategy transaction (Npgsql retry constraint). Ping enqueues one
/// <c>webhook_ping</c> delivery the same drain sends.
/// </summary>
public sealed class WebhookService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ISecretProtector _secrets;
    private readonly IWebhookUrlValidator _urlValidator;
    private readonly ITokenGenerator _tokens;

    private const string WildcardEventType = "*";
    private const string PingEventType = "webhook_ping";
    private const int DefaultDeliveryLimit = 50;
    private const int MaxDeliveryLimit = 100;

    public WebhookService(
        IAppDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ISecretProtector secrets,
        IWebhookUrlValidator urlValidator,
        ITokenGenerator tokens)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _secrets = secrets;
        _urlValidator = urlValidator;
        _tokens = tokens;
    }

    // ----- List a team's subscriptions (§5.5) -----

    public async Task<IReadOnlyList<WebhookSubscriptionDto>> ListAsync(Guid teamId, CancellationToken ct)
    {
        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, ct);
        if (!teamExists)
            throw ServiceException.NotFound("Team not found.");
        _currentUser.RequireTeamAccess(teamId);

        var rows = await _db.WebhookSubscriptions.AsNoTracking()
            .Where(s => s.TeamId == teamId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    // ----- Create a subscription (§5.5) -----

    public async Task<CreateWebhookResponse> CreateAsync(Guid teamId, CreateWebhookRequest request, CancellationToken ct)
    {
        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, ct);
        if (!teamExists)
            throw ServiceException.NotFound("Team not found.");
        _currentUser.RequireTeamAccess(teamId);

        var url = ValidateUrl(request.Url);
        var eventTypes = ValidateEventTypes(request.EventTypes);

        // Generate the signing secret (shown once), encrypt it at rest (recoverable, [ASSUMPTION W3-WH-SECRET]).
        var secret = GenerateSecret();
        var now = _clock.UtcNow;
        var subscription = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            CreatedBy = _currentUser.RequireUserId(),
            Url = url,
            SecretEncrypted = _secrets.Protect(secret),
            EventTypes = eventTypes,
            Active = request.Active ?? true,
            CreatedAt = now,
            ModifiedAt = now
        };

        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            _db.WebhookSubscriptions.Add(subscription);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return new CreateWebhookResponse(ToDto(subscription), secret);
    }

    // ----- Update url/events/active (+ optional rotate secret) (§5.5) -----

    public async Task<UpdateWebhookResponse> UpdateAsync(Guid id, UpdateWebhookRequest request, CancellationToken ct)
    {
        var subscription = await _db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ServiceException.NotFound("Webhook subscription not found.");
        _currentUser.RequireTeamAccess(subscription.TeamId);

        // Validate the requested fields (team is immutable). Omitted fields leave the stored value.
        var newUrl = request.Url is null ? subscription.Url : ValidateUrl(request.Url);
        var newEventTypes = request.EventTypes is null ? subscription.EventTypes : ValidateEventTypes(request.EventTypes);
        var newActive = request.Active ?? subscription.Active;
        var rotate = request.RotateSecret == true;

        var changed =
            !string.Equals(newUrl, subscription.Url, StringComparison.Ordinal) ||
            !string.Equals(newEventTypes, subscription.EventTypes, StringComparison.Ordinal) ||
            newActive != subscription.Active ||
            rotate;

        string? revealedSecret = null;
        if (changed)
        {
            subscription.Url = newUrl;
            subscription.EventTypes = newEventTypes;
            subscription.Active = newActive;
            if (rotate)
            {
                var secret = GenerateSecret();
                subscription.SecretEncrypted = _secrets.Protect(secret);
                revealedSecret = secret;
            }
            subscription.ModifiedAt = _clock.UtcNow;

            await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }

        return new UpdateWebhookResponse(ToDto(subscription), revealedSecret);
    }

    // ----- Delete a subscription (cascades deliveries) (§5.5) -----

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var subscription = await _db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ServiceException.NotFound("Webhook subscription not found.");
        _currentUser.RequireTeamAccess(subscription.TeamId);

        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            // Deliveries CASCADE at the DB (both providers); explicitly remove tracked rows too for provider
            // parity (mirrors LabelService/TicketService cascades).
            var deliveries = await _db.WebhookDeliveries.Where(d => d.SubscriptionId == id).ToListAsync(ct);
            if (deliveries.Count > 0)
                _db.WebhookDeliveries.RemoveRange(deliveries);
            _db.WebhookSubscriptions.Remove(subscription);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    // ----- Delivery audit list (§5.5) -----

    public async Task<WebhookDeliveryListDto> ListDeliveriesAsync(
        Guid subscriptionId, int? limit, string? cursor, CancellationToken ct)
    {
        var subscription = await _db.WebhookSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw ServiceException.NotFound("Webhook subscription not found.");
        _currentUser.RequireTeamAccess(subscription.TeamId);

        var take = Math.Clamp(limit ?? DefaultDeliveryLimit, 1, MaxDeliveryLimit);

        var query = _db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.SubscriptionId == subscriptionId);

        // Keyset pagination on created_at desc, id desc tiebreak. Cursor = "{createdAtTicks}:{id}".
        if (TryParseCursor(cursor, out var cursorCreatedAt, out var cursorId))
        {
            query = query.Where(d =>
                d.CreatedAt < cursorCreatedAt
                || (d.CreatedAt == cursorCreatedAt && d.Id.CompareTo(cursorId) < 0));
        }

        var rows = await query
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > take;
        var page = rows.Take(take).ToList();
        var nextCursor = hasMore && page.Count > 0
            ? $"{page[^1].CreatedAt.Ticks}:{page[^1].Id}"
            : null;

        var items = page.Select(d => new WebhookDeliveryDto(
            d.Id, d.EventType, d.Status, d.Attempts, d.LastStatusCode, d.LastError, d.CreatedAt, d.DeliveredAt))
            .ToList();

        return new WebhookDeliveryListDto(items, hasMore, nextCursor);
    }

    // ----- Ping: enqueue a test delivery (§5.5) -----

    public async Task<WebhookPingResponse> PingAsync(Guid subscriptionId, CancellationToken ct)
    {
        var subscription = await _db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw ServiceException.NotFound("Webhook subscription not found.");
        _currentUser.RequireTeamAccess(subscription.TeamId);

        var now = _clock.UtcNow;
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            @event = PingEventType,
            ping = true,
            subscriptionId = subscription.Id,
            occurredAt = now
        }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        var delivery = new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscription.Id,
            EventType = PingEventType,
            PayloadJson = payload,
            Status = WebhookDeliveryStatusCanonical.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now
        };

        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            _db.WebhookDeliveries.Add(delivery);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return new WebhookPingResponse(delivery.Id);
    }

    // ----- helpers -----

    private string ValidateUrl(string? url)
    {
        if (!_urlValidator.ValidateForSubscribe(url, out var error))
            throw ServiceException.Validation("url", error ?? "The webhook URL is invalid.");
        return url!.Trim();
    }

    /// <summary>
    /// Validate the requested subscribed event types: either the single wildcard <c>"*"</c> or a de-duplicated
    /// list of canonical <see cref="EventType"/> codes. Stored as a comma-joined csv. Empty ⇒ 400.
    /// </summary>
    private static string ValidateEventTypes(IReadOnlyList<string>? requested)
    {
        if (requested is null || requested.Count == 0)
            throw ServiceException.Validation("eventTypes", "At least one event type (or \"*\") is required.");

        var trimmed = requested.Select(t => (t ?? string.Empty).Trim()).Where(t => t.Length > 0).ToList();
        if (trimmed.Count == 0)
            throw ServiceException.Validation("eventTypes", "At least one event type (or \"*\") is required.");

        if (trimmed.Any(t => t == WildcardEventType))
            return WildcardEventType;

        var valid = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in trimmed)
        {
            var canonical = code.ToLowerInvariant();
            if (!EventTypeCanonical.AllCanonical.Contains(canonical))
                throw ServiceException.Validation("eventTypes", $"Unknown event type '{code}'.");
            valid.Add(canonical);
        }

        return string.Join(",", valid);
    }

    /// <summary>Generate a high-entropy signing secret shown once (whsec_ prefix, [ASSUMPTION W3-WH-SIGNATURE]).</summary>
    private string GenerateSecret() => "whsec_" + _tokens.GenerateRawToken();

    private static WebhookSubscriptionDto ToDto(WebhookSubscription s)
        => new(
            s.Id,
            s.TeamId,
            s.Url,
            ParseEventTypes(s.EventTypes),
            s.Active,
            s.CreatedAt,
            s.ModifiedAt);

    private static IReadOnlyList<string> ParseEventTypes(string stored)
    {
        var raw = stored?.Trim();
        if (string.IsNullOrEmpty(raw))
            return Array.Empty<string>();
        if (raw == WildcardEventType)
            return new[] { WildcardEventType };
        return raw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool TryParseCursor(string? cursor, out DateTime createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        if (string.IsNullOrWhiteSpace(cursor))
            return false;
        var parts = cursor.Split(':', 2);
        if (parts.Length != 2)
            return false;
        if (!long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out id))
            return false;
        createdAt = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }
}
