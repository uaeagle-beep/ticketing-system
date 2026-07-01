using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Services;

/// <summary>
/// Personal-access-token (API key) management for the caller (Wave 3, ADR-0021, §5.6). Self BY CONSTRUCTION:
/// every method acts on <c>CurrentUser.RequireUserId()</c> — there is no other user's id in the path, so a
/// user can never address another account's keys (a wrong id on revoke → 404 self-mask, like notifications).
/// The raw key (<c>ptk_&lt;base64url&gt;</c>) is generated + returned ONCE on create; only its hash (via
/// <see cref="ITokenGenerator"/>) + a display prefix are stored. Revoke sets <c>revoked_at</c> (idempotent);
/// a revoked key never authenticates again. Create runs inside the execution-strategy transaction.
/// </summary>
public sealed class ApiKeyService
{
    /// <summary>Distinguishable key prefix so BearerAuthMiddleware can route ptk_ tokens ([ASSUMPTION W3-APIKEY-TRANSPORT]).</summary>
    public const string KeyPrefix = "ptk_";

    private const int NameMax = 100;
    private const int DisplayPrefixLength = 12; // "ptk_" + 8 chars

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ITokenGenerator _tokens;

    public ApiKeyService(IAppDbContext db, IClock clock, ICurrentUser currentUser, ITokenGenerator tokens)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _tokens = tokens;
    }

    // ----- List my keys (§5.6) -----

    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken ct)
    {
        var userId = _currentUser.RequireUserId();
        var rows = await _db.ApiKeys.AsNoTracking()
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    // ----- Create a key (raw shown once, §5.6) -----

    public async Task<CreateApiKeyResponse> CreateAsync(CreateApiKeyRequest request, CancellationToken ct)
    {
        var userId = _currentUser.RequireUserId();

        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw ServiceException.Validation("name", "A key name is required.");
        if (name.Length > NameMax)
            throw ServiceException.Validation("name", $"Name must be at most {NameMax} characters.");

        var scopes = ValidateScopes(request.Scopes);

        // Raw key: ptk_<base64url 32 bytes>. Store only the hash + a short display prefix.
        var rawKey = KeyPrefix + _tokens.GenerateRawToken();
        var now = _clock.UtcNow;
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            TokenHash = _tokens.Hash(rawKey),
            Prefix = rawKey.Length >= DisplayPrefixLength ? rawKey[..DisplayPrefixLength] : rawKey,
            Scopes = scopes,
            CreatedAt = now
        };

        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            _db.ApiKeys.Add(apiKey);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return new CreateApiKeyResponse(ToDto(apiKey), rawKey);
    }

    // ----- Revoke a key (§5.6) -----

    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.RequireUserId();

        // Resolve by id AND user_id = me → 404 self-mask for another user's key id (§5.6).
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId, ct)
            ?? throw ServiceException.NotFound("API key not found.");

        // Idempotent: revoking an already-revoked key is a no-op.
        if (key.RevokedAt is null)
        {
            key.RevokedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    // ----- helpers -----

    /// <summary>
    /// Validate + normalize the requested scopes to a canonical csv. Each must be a known
    /// <see cref="ApiKeyScope"/> code (400 keyed <c>scopes</c>); <c>tickets:write</c> implies read so the
    /// stored set includes read for a write key. At least one scope is required.
    /// </summary>
    private static string ValidateScopes(IReadOnlyList<string>? requested)
    {
        if (requested is null || requested.Count == 0)
            throw ServiceException.Validation("scopes", "At least one scope is required.");

        var parsed = new HashSet<ApiKeyScope>();
        foreach (var raw in requested)
        {
            if (!ApiKeyScopeCanonical.TryParse(raw, out var scope))
                throw ServiceException.Validation("scopes", $"Unknown scope '{raw}'.");
            parsed.Add(scope);
        }

        if (parsed.Count == 0)
            throw ServiceException.Validation("scopes", "At least one scope is required.");

        // write implies read (§5.6): a write key also carries the read scope.
        if (parsed.Contains(ApiKeyScope.TicketsWrite))
            parsed.Add(ApiKeyScope.TicketsRead);

        // Emit in a stable order (read, write).
        var ordered = new List<string>();
        if (parsed.Contains(ApiKeyScope.TicketsRead)) ordered.Add(ApiKeyScopeCanonical.TicketsRead);
        if (parsed.Contains(ApiKeyScope.TicketsWrite)) ordered.Add(ApiKeyScopeCanonical.TicketsWrite);
        return string.Join(",", ordered);
    }

    private static ApiKeyDto ToDto(ApiKey k)
        => new(k.Id, k.Name, k.Prefix, ParseScopes(k.Scopes), k.CreatedAt, k.LastUsedAt, k.RevokedAt);

    internal static IReadOnlyList<string> ParseScopes(string stored)
    {
        var raw = stored?.Trim();
        if (string.IsNullOrEmpty(raw))
            return Array.Empty<string>();
        return raw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
