namespace TicketTracker.Application.Abstractions;

/// <summary>
/// Password hashing port (V2). Production implementation uses Argon2id and never stores
/// or logs the plaintext password.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Produce an encoded (self-describing) hash string for the given password.</summary>
    string Hash(string password);

    /// <summary>Verify a candidate password against a previously produced encoded hash.</summary>
    bool Verify(string password, string encodedHash);
}
