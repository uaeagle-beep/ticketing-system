using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Infrastructure.Security;

/// <summary>
/// Argon2id password hasher (V2, ARCHITECTURE §1). Produces a self-describing PHC-style
/// string ($argon2id$v=19$m=...,t=...,p=...$saltB64$hashB64) so verification needs no
/// external parameter store. Parameters are tuned for ~100-250ms at hackathon scale and
/// are constants here (not env-tuned), per ARCHITECTURE §8. Plaintext is never stored/logged.
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    // Tuning constants (ARCHITECTURE §8).
    private const int MemoryKib = 19456;   // 19 MiB
    private const int Iterations = 2;
    private const int DegreeOfParallelism = 1;
    private const int SaltSize = 16;       // bytes
    private const int HashSize = 32;       // bytes
    private const int Version = 19;        // 0x13, Argon2 v1.3

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = ComputeHash(password, salt, MemoryKib, Iterations, DegreeOfParallelism, HashSize);

        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"$argon2id$v={Version}$m={MemoryKib},t={Iterations},p={DegreeOfParallelism}$" +
            $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    public bool Verify(string password, string encodedHash)
    {
        var parsed = TryParse(encodedHash);
        if (parsed is null) return false;

        var (memory, iterations, parallelism, salt, expected) = parsed.Value;
        var actual = ComputeHash(password, salt, memory, iterations, parallelism, expected.Length);

        // Constant-time comparison.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] ComputeHash(string password, byte[] salt, int memoryKib, int iterations, int parallelism, int hashSize)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };
        return argon2.GetBytes(hashSize);
    }

    private static (int memory, int iterations, int parallelism, byte[] salt, byte[] hash)? TryParse(string encoded)
    {
        // Expected: $argon2id$v=19$m=19456,t=2,p=1$<saltB64>$<hashB64>
        if (string.IsNullOrEmpty(encoded)) return null;
        var parts = encoded.Split('$', StringSplitOptions.RemoveEmptyEntries);
        // parts: [argon2id, v=19, m=...,t=...,p=..., saltB64, hashB64]
        if (parts.Length != 5) return null;
        if (!parts[0].Equals("argon2id", StringComparison.Ordinal)) return null;

        var paramSegment = parts[2];
        int memory = 0, iterations = 0, parallelism = 0;
        foreach (var kv in paramSegment.Split(','))
        {
            var pair = kv.Split('=', 2);
            if (pair.Length != 2 || !int.TryParse(pair[1], out var value)) return null;
            switch (pair[0])
            {
                case "m": memory = value; break;
                case "t": iterations = value; break;
                case "p": parallelism = value; break;
            }
        }
        if (memory <= 0 || iterations <= 0 || parallelism <= 0) return null;

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var hash = Convert.FromBase64String(parts[4]);
            if (salt.Length == 0 || hash.Length == 0) return null;
            return (memory, iterations, parallelism, salt, hash);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
