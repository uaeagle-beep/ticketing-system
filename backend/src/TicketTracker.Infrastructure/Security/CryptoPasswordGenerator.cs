using System.Security.Cryptography;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Infrastructure.Security;

/// <summary>
/// CSPRNG strong-password generator for admin-created accounts and resets ([ПРИПУЩЕННЯ UM-4]).
/// Produces a <see cref="Length"/>-char (16) password drawn uniformly from a mixed alphabet
/// (lower/upper/digit/symbol) using <see cref="RandomNumberGenerator"/>, then guarantees at least
/// one character from each class so it always satisfies a "mixed classes" policy. Rejection sampling
/// avoids modulo bias. The plaintext is returned once and never logged or persisted (R-6).
/// </summary>
public sealed class CryptoPasswordGenerator : IPasswordGenerator
{
    private const int Length = 16;

    private const string Lower = "abcdefghijkmnopqrstuvwxyz";   // no l (ambiguity)
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";    // no I, O
    private const string Digits = "23456789";                  // no 0, 1
    private const string Symbols = "!@#$%^&*-_=+?";
    private static readonly string All = Lower + Upper + Digits + Symbols;

    public string Generate()
    {
        var chars = new char[Length];

        // Guarantee one of each class (positions 0..3), fill the rest from the full alphabet.
        chars[0] = Pick(Lower);
        chars[1] = Pick(Upper);
        chars[2] = Pick(Digits);
        chars[3] = Pick(Symbols);
        for (var i = 4; i < Length; i++)
            chars[i] = Pick(All);

        Shuffle(chars); // remove the fixed class positions
        return new string(chars);
    }

    private static char Pick(string alphabet)
        => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static void Shuffle(char[] buffer)
    {
        // Fisher–Yates with a CSPRNG-backed index.
        for (var i = buffer.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }
    }
}
