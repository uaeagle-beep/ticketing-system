namespace TicketTracker.Application.Dtos;

// API_CONTRACT §5.5 (Wave 3, ADR-0021). Webhook management is M(team). The signing secret is NEVER
// serialized on read — it is returned ONCE on create/rotate inside CreateWebhookResponse.

/// <summary>
/// A webhook subscription as returned by list/create/update — NEVER includes the secret (§5.5). EventTypes
/// is the parsed list of subscribed canonical codes (or the single element <c>"*"</c> for all).
/// </summary>
public sealed record WebhookSubscriptionDto(
    Guid Id,
    Guid TeamId,
    string Url,
    IReadOnlyList<string> EventTypes,
    bool Active,
    DateTime CreatedAt,
    DateTime ModifiedAt);

/// <summary>POST /api/teams/{id}/webhooks body.</summary>
public sealed record CreateWebhookRequest(string? Url, IReadOnlyList<string>? EventTypes, bool? Active);

/// <summary>
/// PUT /api/webhooks/{id} body. All fields optional except that a real url/events/active change advances
/// modifiedAt (no-op diff leaves it). <c>RotateSecret=true</c> returns a fresh secret once.
/// </summary>
public sealed record UpdateWebhookRequest(
    string? Url, IReadOnlyList<string>? EventTypes, bool? Active, bool? RotateSecret);

/// <summary>Create/rotate response: the subscription plus the signing secret shown ONCE (§5.5).</summary>
public sealed record CreateWebhookResponse(WebhookSubscriptionDto Subscription, string Secret);

/// <summary>Update response: the subscription plus the secret ONLY when it was rotated (else null).</summary>
public sealed record UpdateWebhookResponse(WebhookSubscriptionDto Subscription, string? Secret);

/// <summary>
/// A delivery audit row (§5.5) — excludes the payload body by default to keep the list light. Includes the
/// last HTTP status + error for troubleshooting.
/// </summary>
public sealed record WebhookDeliveryDto(
    Guid Id,
    string EventType,
    string Status,
    int Attempts,
    int? LastStatusCode,
    string? LastError,
    DateTime CreatedAt,
    DateTime? DeliveredAt);

/// <summary>Keyset-paged delivery list (§5.5), newest-first.</summary>
public sealed record WebhookDeliveryListDto(
    IReadOnlyList<WebhookDeliveryDto> Items,
    bool HasMore,
    string? NextCursor);

/// <summary>POST /api/webhooks/{id}/ping response (§5.5): the enqueued test delivery id.</summary>
public sealed record WebhookPingResponse(Guid DeliveryId);
