namespace TicketTracker.Domain.Entities;

/// <summary>
/// A registered account. Email uniqueness is case-insensitive + trim-insensitive,
/// enforced via the normalized companion column <see cref="EmailNormalized"/> (V1, A6).
/// Password is stored only as an Argon2id PHC hash, never plaintext (V2).
/// </summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Original-case display value as entered (trimmed).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>trim(lower(email)) — the case-insensitive uniqueness key.</summary>
    public string EmailNormalized { get; set; } = string.Empty;

    /// <summary>Argon2id encoded (PHC string) hash. Never the raw password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Gate for business access (V5, A1). False until a token is consumed.</summary>
    public bool EmailVerified { get; set; }

    /// <summary>
    /// Global admin privilege (ADR-0007). An admin ignores team scoping entirely. Only an admin may
    /// change this. Existing users are promoted to admin by the AddUserManagement migration (ASR-5).
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Hard access denial (ADR-0007, ASR-2). A blocked user cannot log in even with the correct
    /// password, cannot reset/change password, and all of their sessions are purged on block.
    /// </summary>
    public bool IsBlocked { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<EmailVerificationToken> VerificationTokens { get; set; } = new List<EmailVerificationToken>();
    public ICollection<Session> Sessions { get; set; } = new List<Session>();

    /// <summary>Team memberships (ADR-0007). Ignored while <see cref="IsAdmin"/> is true.</summary>
    public ICollection<UserTeam> Memberships { get; set; } = new List<UserTeam>();
}
