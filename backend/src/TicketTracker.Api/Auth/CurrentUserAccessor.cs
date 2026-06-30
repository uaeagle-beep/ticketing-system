using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;

namespace TicketTracker.Api.Auth;

/// <summary>
/// Scoped holder for the authenticated user's id. The bearer-auth middleware sets it after
/// validating the session; Application services read it via <see cref="ICurrentUser"/> so they
/// stay HTTP-agnostic.
/// </summary>
public sealed class CurrentUserAccessor : ICurrentUser
{
    public Guid? UserId { get; private set; }

    public void Set(Guid userId) => UserId = userId;

    public Guid RequireUserId()
        => UserId ?? throw ServiceException.Unauthorized();
}
