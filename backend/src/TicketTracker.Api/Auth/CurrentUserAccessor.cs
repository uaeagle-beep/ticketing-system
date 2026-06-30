using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;

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

    public void Set(Guid userId, bool isAdmin, IReadOnlySet<Guid> teamIds)
    {
        UserId = userId;
        IsAdmin = isAdmin;
        TeamIds = teamIds;
    }

    public Guid RequireUserId()
        => UserId ?? throw ServiceException.Unauthorized();

    public void RequireAdmin()
    {
        if (!IsAdmin)
            throw new ServiceException(ServiceErrorCode.Forbidden, "Admin access required.");
    }

    public bool CanAccessTeam(Guid teamId) => IsAdmin || TeamIds.Contains(teamId);

    public void RequireTeamAccess(Guid teamId)
    {
        if (!CanAccessTeam(teamId))
            throw new ServiceException(ServiceErrorCode.Forbidden, "You do not have access to this team.");
    }
}
