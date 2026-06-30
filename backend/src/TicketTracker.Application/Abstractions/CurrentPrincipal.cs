namespace TicketTracker.Application.Abstractions;

/// <summary>
/// The result of resolving a bearer token: the authenticated user's id plus the authorization
/// context (admin flag + membership team ids) the API middleware uses to populate
/// <see cref="ICurrentUser"/> (ADR-0007). Distinct from the persistence <c>User</c> entity so the
/// auth pipeline carries only what it needs.
/// </summary>
public sealed record CurrentPrincipal(Guid UserId, bool IsAdmin, IReadOnlyList<Guid> TeamIds);
