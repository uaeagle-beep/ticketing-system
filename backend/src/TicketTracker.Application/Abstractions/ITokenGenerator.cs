namespace TicketTracker.Application.Abstractions;

/// <summary>
/// Generates high-entropy opaque tokens (session bearer tokens and email verification
/// tokens) and computes their storage hash (SHA-256 hex). The raw token is returned to
/// the caller once; only the hash is persisted (ADR-0001, ADR-0006).
/// </summary>
public interface ITokenGenerator
{
    /// <summary>Generate a CSPRNG token (>= 256 bits) encoded as a URL-safe base64 string.</summary>
    string GenerateRawToken();

    /// <summary>Compute the SHA-256 hex (64 lowercase chars) of a raw token for storage/lookup.</summary>
    string Hash(string rawToken);
}
