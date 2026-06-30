using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;

namespace TicketTracker.Tests.Fakes;

/// <summary>Test <see cref="ICurrentUser"/> with a fixed authenticated user id.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public FakeCurrentUser(Guid userId) => UserId = userId;

    public Guid? UserId { get; set; }

    public Guid RequireUserId() => UserId ?? throw ServiceException.Unauthorized();
}
