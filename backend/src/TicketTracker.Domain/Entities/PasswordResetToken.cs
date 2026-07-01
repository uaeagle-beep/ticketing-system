namespace TicketTracker.Domain.Entities;

/// <summary>
/// Single-use, time-bounded password-reset token (F-01, ADR-0010). Structurally a twin of
/// <see cref="EmailVerificationToken"/> but in its own table with a distinct TTL
/// (PASSWORD_RESET_TTL_HOURS, default 1) so the two flows' invariants stay clean and cannot
/// cross-accept. Only the SHA-256 hash of the raw token is stored; the raw token appears only in
/// the emailed link. Expiry boundary: now &gt;= ExpiresAt =&gt; expired (A31).
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>SHA-256 hex (64 chars) of the raw token. Raw token never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>CreatedAt + PASSWORD_RESET_TTL_HOURS (default 1).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set atomically on successful reset; non-null =&gt; already used (single-use).</summary>
    public DateTime? ConsumedAt { get; set; }
}
