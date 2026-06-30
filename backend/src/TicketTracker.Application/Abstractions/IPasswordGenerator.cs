namespace TicketTracker.Application.Abstractions;

/// <summary>
/// Generates a strong random password for admin-created accounts and password resets
/// ([ПРИПУЩЕННЯ UM-4/UM-5]). The implementation uses a CSPRNG, is ≥ 16 chars with mixed
/// character classes, and the plaintext is returned to the caller exactly once — never logged,
/// never persisted (only its Argon2id hash is stored).
/// </summary>
public interface IPasswordGenerator
{
    /// <summary>Produce a fresh strong password (≥ 16 chars, mixed classes, CSPRNG).</summary>
    string Generate();
}
