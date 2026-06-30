namespace TicketTracker.Application.Abstractions;

/// <summary>
/// Provides the authenticated user's id to Application services (e.g. ticket.created_by,
/// comment.author). Implemented in the API layer from the bearer-auth middleware so the
/// services stay HTTP-agnostic. <see cref="UserId"/> is null when unauthenticated.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    /// <summary>The authenticated user's id, or throws Unauthorized if absent.</summary>
    Guid RequireUserId();
}
