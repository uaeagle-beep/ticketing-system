using System.Net.Mail;

namespace TicketTracker.Application.Validation;

/// <summary>
/// Syntactic email validation (A6). We do not enforce deliverability — only a reasonable
/// RFC-ish shape — and rely on <see cref="MailAddress"/> for parsing. Normalization
/// (trim + lowercase) for the uniqueness key is done separately in <see cref="Normalization"/>.
/// </summary>
public static class EmailValidator
{
    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var trimmed = email.Trim();
        if (trimmed.Length > FieldLimits.EmailMax) return false;
        // Disallow internal whitespace and display-name forms.
        if (trimmed.Any(char.IsWhiteSpace)) return false;
        if (!trimmed.Contains('@')) return false;

        try
        {
            var addr = new MailAddress(trimmed);
            // MailAddress accepts display-name forms; require the address to equal the input.
            return string.Equals(addr.Address, trimmed, StringComparison.OrdinalIgnoreCase)
                   && addr.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
