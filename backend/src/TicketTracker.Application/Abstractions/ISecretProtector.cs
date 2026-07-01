namespace TicketTracker.Application.Abstractions;

/// <summary>
/// Symmetric, reversible protection of a small secret at rest (Wave 3, ADR-0021, [ASSUMPTION W3-WH-SECRET]).
/// Unlike passwords / API-keys (verify-only → one-way hash), a webhook signing secret must be RECOVERABLE
/// because the delivery worker re-signs every request with it — so it is stored encrypted, not hashed. The
/// production implementation is AES-256-GCM with a random per-secret nonce, keyed by <c>WEBHOOK_SIGNING_KEY</c>
/// (env; fail-fast in Production). A DB leak alone cannot reveal signing secrets (the key lives outside the DB).
/// </summary>
public interface ISecretProtector
{
    /// <summary>Encrypt a plaintext secret to a compact, self-describing token (nonce + tag + ciphertext).</summary>
    string Protect(string plaintext);

    /// <summary>Decrypt a token produced by <see cref="Protect"/>. Throws on tamper / wrong key.</summary>
    string Unprotect(string protectedValue);
}
