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

    /// <summary>
    /// Optional display name. Trimmed on save; blank/whitespace is stored as null. When null,
    /// the UI falls back to <see cref="Email"/> as the display value. Email remains the key for
    /// login and account management; the name is purely cosmetic.
    /// </summary>
    public string? Name { get; set; }

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

    /// <summary>
    /// Global email-notifications toggle (Wave 2, ADR-0013 / §6.8). Default true. Suppresses email ONLY —
    /// in-app notifications are always created. The outbox worker skips email-off recipients and marks
    /// their rows emailed (no send) so they never backlog. Read/set via GET/PUT /api/me/notification-settings.
    /// </summary>
    public bool EmailNotificationsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public ICollection<EmailVerificationToken> VerificationTokens { get; set; } = new List<EmailVerificationToken>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    public ICollection<Session> Sessions { get; set; } = new List<Session>();

    /// <summary>Team memberships (ADR-0007). Ignored while <see cref="IsAdmin"/> is true.</summary>
    public ICollection<UserTeam> Memberships { get; set; } = new List<UserTeam>();
}
