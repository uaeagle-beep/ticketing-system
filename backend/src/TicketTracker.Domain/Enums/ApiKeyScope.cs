namespace TicketTracker.Domain.Enums;

/// <summary>
/// Coarse scopes an API key may grant on the public <c>/api/v1</c> surface (Wave 3, ADR-0021,
/// [ASSUMPTION W3-APIKEY-SCOPE]). Deliberately minimal — <c>tickets:read</c> (board/tickets/comments
/// read) and <c>tickets:write</c> (create/update/patch-state/comment). Write IMPLIES read. There is no
/// delete/admin/attachment scope: a leaked key can never destroy data, reach <c>/api/admin/*</c>, or
/// exfiltrate blobs. Stored on <c>api_keys.scopes</c> as a csv of the canonical codes.
/// </summary>
public enum ApiKeyScope
{
    /// <summary>Read the board, tickets, and comments via <c>/api/v1</c> (GET).</summary>
    TicketsRead,

    /// <summary>Create/update/patch-state a ticket and add a comment via <c>/api/v1</c> (write implies read).</summary>
    TicketsWrite
}

/// <summary>
/// Single source of truth for the canonical string form of <see cref="ApiKeyScope"/> (mirrors
/// <c>EnumCanonical</c> / <c>EventTypeCanonical</c>). Scopes are stored + compared as canonical text.
/// </summary>
public static class ApiKeyScopeCanonical
{
    public const string TicketsRead = "tickets:read";
    public const string TicketsWrite = "tickets:write";

    public static string ToCanonical(ApiKeyScope scope) => scope switch
    {
        ApiKeyScope.TicketsRead => TicketsRead,
        ApiKeyScope.TicketsWrite => TicketsWrite,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown API key scope.")
    };

    public static bool TryParse(string? value, out ApiKeyScope scope)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case TicketsRead:
                scope = ApiKeyScope.TicketsRead;
                return true;
            case TicketsWrite:
                scope = ApiKeyScope.TicketsWrite;
                return true;
            default:
                scope = default;
                return false;
        }
    }

    /// <summary>All canonical scope codes.</summary>
    public static readonly IReadOnlyList<string> AllCanonical = new[] { TicketsRead, TicketsWrite };
}
