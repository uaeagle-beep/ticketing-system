namespace TicketTracker.Domain.Entities;

/// <summary>
/// A stateful opaque bearer-token session (ADR-0001). Only the SHA-256 hash of the
/// opaque token is stored; the raw token is returned to the client once at login.
/// Logout deletes the row, so a reused token returns 401 (EC15).
/// </summary>
public class Session
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>SHA-256 hex (64 chars) of the opaque bearer token. Unique. Raw token never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>CreatedAt + SESSION_TTL_HOURS.</summary>
    public DateTime ExpiresAt { get; set; }
}
