namespace TicketTracker.Domain.Entities;

/// <summary>
/// A personal access token (Wave 3, ADR-0021, §4.5). The raw key (<c>ptk_&lt;32 bytes base64url&gt;</c>) is
/// shown ONCE on create; only its <see cref="TokenHash"/> (SHA-256(HMAC pepper) via <c>ITokenGenerator</c>,
/// like sessions/verification tokens — verify-only ⇒ one-way hash) and a display <see cref="Prefix"/> are
/// stored. Auth hashes the presented key and matches <c>token_hash</c> with <c>revoked_at IS NULL</c>. A key
/// is owned by its user (CASCADE), scope-limited to the <c>/api/v1</c> surface, and can never be an
/// admin/destructive credential. <see cref="LastUsedAt"/> is a throttled write for abuse visibility.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>User label (e.g. "CI pipeline").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256(HMAC) of the raw key; the raw is shown once and never stored. UNIQUE.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>First chars of the raw key (e.g. "ptk_ab12cd34") — display in the list + narrows lookup.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Csv of canonical ApiKeyScope codes; write implies read.</summary>
    public string Scopes { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Updated at most once/60s per key (throttle, §7.3); null until first use.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Revoke = set now; a revoked key never authenticates. Null = active.</summary>
    public DateTime? RevokedAt { get; set; }
}
