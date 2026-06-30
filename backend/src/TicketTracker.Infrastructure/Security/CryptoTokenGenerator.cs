using System.Security.Cryptography;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Infrastructure.Security;

/// <summary>
/// CSPRNG opaque-token generator + keyed (HMAC-SHA256) storage hashing (ADR-0001, ADR-0006).
/// Tokens are 256-bit and base64url-encoded. Only the keyed hash hex of a token is persisted,
/// so a DB read cannot reconstruct a live token, and the documented <c>AUTH_TOKEN_SECRET</c>
/// pepper (ARCHITECTURE §8) is mixed into every stored hash. The same keyed hash is used when a
/// token is issued and when it is verified, so lookups still match. Output stays 64 lowercase
/// hex chars (same column size as the prior unsalted SHA-256 — no migration needed).
/// </summary>
public sealed class CryptoTokenGenerator : ITokenGenerator
{
    private const int TokenBytes = 32; // 256 bits

    private readonly byte[] _key;

    /// <param name="secret">
    /// HMAC pepper (env <c>AUTH_TOKEN_SECRET</c>). Must be non-empty; the DI registration
    /// enforces fail-fast in Production and supplies a built-in dev key otherwise.
    /// </param>
    public CryptoTokenGenerator(string secret)
    {
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("Token secret must not be empty.", nameof(secret));
        _key = System.Text.Encoding.UTF8.GetBytes(secret);
    }

    public string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return Base64UrlEncode(bytes);
    }

    public string Hash(string rawToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawToken);
        var hash = HMACSHA256.HashData(_key, bytes);
        return Convert.ToHexStringLower(hash); // 64 lowercase hex chars
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
