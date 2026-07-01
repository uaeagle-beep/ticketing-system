using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Services;

/// <summary>
/// Resolves a raw API key (<c>ptk_…</c>) to its authenticated principal (Wave 3, ADR-0021, §7.3). Called by
/// <c>BearerAuthMiddleware</c> when the bearer token is <c>ptk_</c>-prefixed; a session token keeps its
/// existing <see cref="AuthService.ResolveSessionUserAsync"/> path. Hashes the presented key (SHA-256(HMAC
/// pepper) via <see cref="ITokenGenerator"/>, like sessions) and looks it up by <c>token_hash</c> with
/// <c>revoked_at IS NULL</c> — a single indexed query. Returns null on any miss (unknown/revoked key, or a
/// blocked/unverified owner). The principal carries the owner's LIVE admin flag + membership team ids
/// (re-read each request, no caching) plus the key's canonical scopes. <see cref="LastUsedAt"/> is updated
/// at most once/60s per key (throttled write, §7.3) for abuse visibility.
/// </summary>
public sealed class ApiKeyAuthenticator
{
    /// <summary>Throttle window for the last_used_at write (§7.3): at most one write per key per minute.</summary>
    private static readonly TimeSpan LastUsedThrottle = TimeSpan.FromSeconds(60);

    private readonly IAppDbContext _db;
    private readonly ITokenGenerator _tokens;
    private readonly IClock _clock;

    public ApiKeyAuthenticator(IAppDbContext db, ITokenGenerator tokens, IClock clock)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
    }

    /// <summary>
    /// Resolve a raw <c>ptk_</c> key to its principal, or null on any miss. Updates <c>last_used_at</c> (throttled).
    /// </summary>
    public async Task<ApiKeyPrincipal?> ResolveAsync(string rawKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return null;

        var hash = _tokens.Hash(rawKey.Trim());
        var key = await _db.ApiKeys
            .FirstOrDefaultAsync(k => k.TokenHash == hash && k.RevokedAt == null, ct);
        if (key is null)
            return null;

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == key.UserId)
            .Select(u => new { u.Id, u.IsAdmin, u.IsBlocked, u.EmailVerified })
            .FirstOrDefaultAsync(ct);

        // A key never authenticates for a missing / blocked / unverified owner (defence-in-depth, §7.3).
        if (user is null || user.IsBlocked || !user.EmailVerified)
            return null;

        // Live authz: read the owner's current memberships each request (no caching, §7.3).
        var teamIds = await _db.UserTeams.AsNoTracking()
            .Where(m => m.UserId == user.Id)
            .Select(m => m.TeamId)
            .ToListAsync(ct);

        var scopes = ApiKeyService.ParseScopes(key.Scopes)
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        // Throttled last_used_at write (§7.3): only when unset or older than the throttle window.
        var now = _clock.UtcNow;
        if (key.LastUsedAt is null || now - key.LastUsedAt.Value >= LastUsedThrottle)
        {
            key.LastUsedAt = now;
            await _db.SaveChangesAsync(ct);
        }

        return new ApiKeyPrincipal(user.Id, user.IsAdmin, teamIds, scopes);
    }
}

/// <summary>
/// The result of resolving an API key: the owner's live authz context (admin flag + membership team ids)
/// plus the key's canonical scopes. Distinct from <c>CurrentPrincipal</c> (sessions) so the API-key path
/// carries scopes + the is-API-key marker.
/// </summary>
public sealed record ApiKeyPrincipal(
    Guid UserId,
    bool IsAdmin,
    IReadOnlyList<Guid> TeamIds,
    IReadOnlySet<string> Scopes);
