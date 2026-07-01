using System.Security.Cryptography;
using System.Text;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Infrastructure.Security;

/// <summary>
/// AES-256-GCM implementation of <see cref="ISecretProtector"/> (Wave 3, ADR-0021, [ASSUMPTION W3-WH-SECRET]).
/// Encrypts a webhook signing secret with a symmetric key derived from <c>WEBHOOK_SIGNING_KEY</c> (SHA-256 of
/// the configured value → a stable 256-bit key, so any key length works). A fresh 12-byte nonce is generated
/// per <see cref="Protect"/> and prepended to the ciphertext; the 16-byte GCM tag follows. Layout of the
/// base64 token: <c>nonce(12) || tag(16) || ciphertext</c>. GCM's authentication makes tamper / wrong-key
/// decryption throw. Unlike a one-way hash (passwords/API-keys), this is REVERSIBLE — the delivery worker
/// must recover the secret to re-sign each request.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceBytes = 12; // AES-GCM standard nonce
    private const int TagBytes = 16;   // AES-GCM standard tag

    private readonly byte[] _key;

    /// <param name="signingKey">
    /// The env <c>WEBHOOK_SIGNING_KEY</c>. Must be non-empty (the DI registration enforces fail-fast in
    /// Production and supplies a built-in dev key otherwise). Hashed to a fixed 256-bit key so any length works.
    /// </param>
    public AesGcmSecretProtector(string signingKey)
    {
        if (string.IsNullOrEmpty(signingKey))
            throw new ArgumentException("Webhook signing key must not be empty.", nameof(signingKey));
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(signingKey)); // 32 bytes → AES-256
    }

    public string Protect(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagBytes];

        using (var aes = new AesGcm(_key, TagBytes))
            aes.Encrypt(nonce, plainBytes, cipher, tag);

        var packed = new byte[NonceBytes + TagBytes + cipher.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, NonceBytes);
        Buffer.BlockCopy(tag, 0, packed, NonceBytes, TagBytes);
        Buffer.BlockCopy(cipher, 0, packed, NonceBytes + TagBytes, cipher.Length);
        return Convert.ToBase64String(packed);
    }

    public string Unprotect(string protectedValue)
    {
        var packed = Convert.FromBase64String(protectedValue);
        if (packed.Length < NonceBytes + TagBytes)
            throw new CryptographicException("Protected secret is malformed.");

        var nonce = packed.AsSpan(0, NonceBytes);
        var tag = packed.AsSpan(NonceBytes, TagBytes);
        var cipher = packed.AsSpan(NonceBytes + TagBytes);
        var plain = new byte[cipher.Length];

        using (var aes = new AesGcm(_key, TagBytes))
            aes.Decrypt(nonce, cipher, tag, plain); // throws on tamper / wrong key

        return Encoding.UTF8.GetString(plain);
    }
}
