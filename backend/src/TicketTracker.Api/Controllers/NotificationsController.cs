using Microsoft.AspNetCore.Mvc;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// In-app notifications for the current user (Wave 2, API_CONTRACT §8, ADR-0013). Self-scoped BY
/// CONSTRUCTION — there is NO user id in any path; the target is always the authenticated recipient
/// (strongest anti-IDOR, same posture as <c>/api/me/*</c>). Not public and not under /api/admin/*, so
/// <c>BearerAuthMiddleware</c> already requires a valid, verified, non-blocked session.
/// </summary>
[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly NotificationService _notifications;

    public NotificationsController(NotificationService notifications) => _notifications = notifications;

    // ----- List my notifications (paged, newest-first) (§5.3) -----
    [HttpGet]
    public async Task<ActionResult<NotificationListDto>> List(
        [FromQuery] int? limit, [FromQuery] string? cursor, CancellationToken ct)
        => Ok(await _notifications.ListAsync(limit, cursor, ct));

    // ----- My unread count (cheap poll target) (§5.3) -----
    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount(CancellationToken ct)
        => Ok(await _notifications.UnreadCountAsync(ct));

    // ----- Mark one read (self-owned; another user's id → 404) (§5.3) -----
    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<UnreadCountDto>> MarkRead(Guid id, CancellationToken ct)
        => Ok(await _notifications.MarkReadAsync(id, ct));

    // ----- Mark all mine read (§5.3) -----
    [HttpPost("read-all")]
    public async Task<ActionResult<UnreadCountDto>> MarkAllRead(CancellationToken ct)
        => Ok(await _notifications.MarkAllReadAsync(ct));
}
