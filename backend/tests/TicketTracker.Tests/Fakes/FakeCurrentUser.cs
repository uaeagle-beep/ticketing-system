using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;

namespace TicketTracker.Tests.Fakes;

/// <summary>
/// Test <see cref="ICurrentUser"/> (USER_MANAGEMENT_DESIGN §6.5). Defaults to an admin so existing
/// service-level unit tests exercise the business rule (modified_at, epic mismatch, etc.) rather than
/// authorization; authz tests set <see cref="IsAdmin"/>=false + <see cref="TeamIds"/> explicitly.
/// </summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public FakeCurrentUser(Guid userId, bool isAdmin = true, IReadOnlySet<Guid>? teamIds = null)
    {
        UserId = userId;
        IsAdmin = isAdmin;
        TeamIds = teamIds ?? new HashSet<Guid>();
    }

    public Guid? UserId { get; set; }
    public bool IsAdmin { get; set; }
    public IReadOnlySet<Guid> TeamIds { get; set; } = new HashSet<Guid>();
    public bool IsApiKey { get; set; }
    public IReadOnlySet<string> Scopes { get; set; } = new HashSet<string>();

    public Guid RequireUserId() => UserId ?? throw ServiceException.Unauthorized();

    public void RequireScope(string requiredScope)
    {
        if (!IsApiKey || Scopes.Contains(requiredScope))
            return;
        throw ServiceException.InsufficientScope();
    }

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
