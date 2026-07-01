namespace TicketTracker.Domain.Entities;

/// <summary>
/// A team's outbound webhook subscription (Wave 3, ADR-0021, §4.3). A subscription is owned by its team
/// (CASCADE); <see cref="CreatedBy"/> preserves authorship (RESTRICT). On each matching, active event the
/// <c>WebhookEnqueuer</c> inserts one <see cref="WebhookDelivery"/> row; the delivery worker signs and
/// sends it. The signing <see cref="SecretEncrypted"/> is stored AES-GCM-encrypted (recoverable, because
/// the worker must re-sign every delivery) via <c>ISecretProtector</c> — NEVER hashed, NEVER serialized
/// back. The raw secret is shown once on create/rotate.
/// </summary>
public class WebhookSubscription
{
    public Guid Id { get; set; }

    public Guid TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>The user who wired the integration (RESTRICT — preserve authorship).</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>The subscriber endpoint. https:// (SSRF policy §7.4); validated on create/update.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>AES-GCM ciphertext of the signing secret (ISecretProtector); never returned to clients.</summary>
    public string SecretEncrypted { get; set; } = string.Empty;

    /// <summary>Subscribed canonical EventType codes as csv; <c>"*"</c> = all events.</summary>
    public string EventTypes { get; set; } = string.Empty;

    /// <summary>Disabled subscriptions enqueue nothing.</summary>
    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    /// <summary>Advances on a real url/events/active/secret change (no-op diff leaves it, like team rename).</summary>
    public DateTime ModifiedAt { get; set; }

    /// <summary>Owned delivery audit rows (CASCADE with the subscription).</summary>
    public ICollection<WebhookDelivery> Deliveries { get; set; } = new List<WebhookDelivery>();
}
