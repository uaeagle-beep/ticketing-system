namespace TicketTracker.Domain.Entities;

/// <summary>
/// Single-use, time-bounded email verification token (V3, V4, ADR-0006).
/// Only the SHA-256 hash of the raw token is stored; the raw token appears only
/// in the emailed link. Expiry boundary: now &gt;= ExpiresAt =&gt; expired (A31).
/// </summary>
public class EmailVerificationToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>SHA-256 hex (64 chars) of the raw token. Raw token never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>CreatedAt + TOKEN_TTL_HOURS (default 24).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set atomically on successful verify; non-null =&gt; already used (single-use).</summary>
    public DateTime? ConsumedAt { get; set; }
}
