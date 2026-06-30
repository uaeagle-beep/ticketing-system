using System.Globalization;

namespace TicketTracker.Application.Validation;

/// <summary>
/// Centralized normalization helpers used for uniqueness keys and for the uniform
/// "is this an actual change?" comparison that drives modified_at semantics
/// (ARCHITECTURE §6.2, A19). Normalization must be identical everywhere to be testable.
/// </summary>
public static class Normalization
{
    /// <summary>Trim only (display value).</summary>
    public static string Trim(string? value) => (value ?? string.Empty).Trim();

    /// <summary>trim(lower(value)) using invariant culture — the case-insensitive key.</summary>
    public static string NormalizeKey(string? value)
        => Trim(value).ToLower(CultureInfo.InvariantCulture);

    /// <summary>True when the trimmed value is empty (whitespace-only inputs are rejected, EC1).</summary>
    public static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Normalize an optional description/free-text: trims; an empty result becomes null
    /// so empty string and null compare equal for no-op detection (A12).
    /// </summary>
    public static string? NormalizeOptionalText(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
