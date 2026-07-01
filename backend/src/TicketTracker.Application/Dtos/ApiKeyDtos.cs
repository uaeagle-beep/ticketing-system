namespace TicketTracker.Application.Dtos;

// API_CONTRACT §5.6 (Wave 3, ADR-0021). API-key management is Self (no id in the create/list path). The raw
// key is NEVER serialized on read — it is returned ONCE on create inside CreateApiKeyResponse.

/// <summary>
/// An API key as returned by list/create — NEVER the raw key or the hash (§5.6). <c>Prefix</c> is a short
/// display fragment (e.g. "ptk_ab12cd34"). <c>RevokedAt</c> null = active.
/// </summary>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    DateTime? RevokedAt);

/// <summary>POST /api/me/api-keys body.</summary>
public sealed record CreateApiKeyRequest(string? Name, IReadOnlyList<string>? Scopes);

/// <summary>Create response: the key metadata plus the raw key shown ONCE (§5.6).</summary>
public sealed record CreateApiKeyResponse(ApiKeyDto Key, string Secret);
