using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Api.Auth;

/// <summary>
/// Scoped holder for the authenticated principal's identity + authorization context (ADR-0007).
/// The bearer-auth middleware sets it after validating the session and loading the user's admin flag
/// and team memberships; Application services read it via <see cref="ICurrentUser"/> so they stay
/// HTTP-agnostic. Authorization decisions (<see cref="RequireAdmin"/>, <see cref="RequireTeamAccess"/>)
/// are made here as the single, testable choke point.
/// </summary>
public sealed class CurrentUserAccessor : ICurrentUser
{
    public Guid? UserId { get; private set; }
    public bool IsAdmin { get; private set; }
    public IReadOnlySet<Guid> TeamIds { get; private set; } = new HashSet<Guid>();
    public bool IsApiKey { get; private set; }
    public IReadOnlySet<string> Scopes { get; private set; } = new HashSet<string>();

    /// <summary>Populate a SESSION principal (the existing path). Not an API-key request; no scopes.</summary>
    public void Set(Guid userId, bool isAdmin, IReadOnlySet<Guid> teamIds)
    {
        UserId = userId;
        IsAdmin = isAdmin;
        TeamIds = teamIds;
        IsApiKey = false;
        Scopes = new HashSet<string>();
    }

    /// <summary>
    /// Populate an API-KEY principal (Wave 3, ADR-0021): the owner's live admin flag + memberships plus the
    /// key's granted scopes, marked as an API-key request so the v1 scope gate applies.
    /// </summary>
    public void SetApiKey(Guid userId, bool isAdmin, IReadOnlySet<Guid> teamIds, IReadOnlySet<string> scopes)
    {
        UserId = userId;
        IsAdmin = isAdmin;
        TeamIds = teamIds;
        IsApiKey = true;
        Scopes = scopes;
    }

    public Guid RequireUserId()
        => UserId ?? throw ServiceException.Unauthorized();

    public void RequireScope(string requiredScope)
    {
        // Session requests never come through the v1 scope gate; only API-key requests are constrained.
        if (!IsApiKey)
            return;

        if (HasScope(requiredScope))
            return;

        throw ServiceException.InsufficientScope(
            $"This API key does not have the required '{requiredScope}' scope.");
    }

    /// <summary>Scope membership with write-implies-read: tickets:write also satisfies tickets:read.</summary>
    private bool HasScope(string requiredScope)
    {
        if (Scopes.Contains(requiredScope))
            return true;
        return requiredScope == ApiKeyScopeCanonical.TicketsRead
               && Scopes.Contains(ApiKeyScopeCanonical.TicketsWrite);
    }

    public void RequireAdmin()
    {
        if (!IsAdmin)
            throw new ServiceException(ServiceErrorCode.Forbidden, "Admin access required.");
    }

    /// <summary>
    /// True when the principal may act on <paramref name="teamId"/>. A SESSION principal keeps the normal
    /// breadth (admin sees all; a member iff a member of that team). An API-KEY principal (SEC-6, PO decision:
    /// RESTRICT) is scoped to the owner's EXPLICIT UserTeam memberships ONLY — the admin breadth is NOT applied
    /// to key requests, so a member-less admin's key has no team access (intended least-privilege outcome).
    /// Admin ENDPOINTS remain unreachable to keys regardless (the middleware rejects <c>ptk_</c> off /api/v1);
    /// this only narrows team SCOPING.
    /// </summary>
    public bool CanAccessTeam(Guid teamId)
    {
        if (IsApiKey)
            return TeamIds.Contains(teamId); // membership-only for API keys (admin breadth does NOT apply)
        return IsAdmin || TeamIds.Contains(teamId);
    }

    public void RequireTeamAccess(Guid teamId)
    {
        if (!CanAccessTeam(teamId))
            throw new ServiceException(ServiceErrorCode.Forbidden, "You do not have access to this team.");
    }
}
