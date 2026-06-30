namespace TicketTracker.Application.Abstractions;

/// <summary>
/// Provides the authenticated user's id to Application services (e.g. ticket.created_by,
/// comment.author). Implemented in the API layer from the bearer-auth middleware so the
/// services stay HTTP-agnostic. <see cref="UserId"/> is null when unauthenticated.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    /// <summary>Global admin privilege (ADR-0007). Admin ignores team scoping entirely.</summary>
    bool IsAdmin { get; }

    /// <summary>Team ids the user belongs to; empty for unauthenticated or team-less users (ignored while admin).</summary>
    IReadOnlySet<Guid> TeamIds { get; }

    /// <summary>The authenticated user's id, or throws Unauthorized if absent.</summary>
    Guid RequireUserId();

    /// <summary>Throws 403 forbidden if the user is not an admin (admin-zone gate).</summary>
    void RequireAdmin();

    /// <summary>True when the user may act on <paramref name="teamId"/>: admin always; member iff a member of that team.</summary>
    bool CanAccessTeam(Guid teamId);

    /// <summary>Throws 403 forbidden if <see cref="CanAccessTeam"/> is false (team-scope gate).</summary>
    void RequireTeamAccess(Guid teamId);
}
