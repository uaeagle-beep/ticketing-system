using Microsoft.AspNetCore.Mvc;
using TicketTracker.Api.Auth;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// Self-service account endpoints (API_CONTRACT §4.5, F-04). Self-only BY CONSTRUCTION: there is NO
/// user id in the path — the target is always <c>CurrentUserAccessor.RequireUserId()</c>, so a user
/// can never address another account (strongest anti-IDOR posture, ADR-0010 §D). Not public and not
/// under /api/admin/*, so <c>BearerAuthMiddleware</c> already requires a valid, verified, non-blocked
/// session — no middleware change. Controllers stay thin (DTO ↔ service + status codes).
/// </summary>
[ApiController]
[Route("api/me")]
public sealed class MeController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly CurrentUserAccessor _currentUser;
    private readonly NotificationService _notifications;

    public MeController(AuthService auth, CurrentUserAccessor currentUser, NotificationService notifications)
    {
        _auth = auth;
        _currentUser = currentUser;
        _notifications = notifications;
    }

    // ----- Set/clear own display name (§4.5) -----
    [HttpPut("profile")]
    public async Task<ActionResult<UserDto>> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken ct)
        => Ok(await _auth.UpdateOwnProfileAsync(
            _currentUser.RequireUserId(), request ?? new UpdateProfileRequest(null), ct));

    // ----- Change own password (current-password re-auth, §4.5) -----
    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        // Pass the current raw bearer token so the service can keep THIS session and purge the others.
        await _auth.ChangeOwnPasswordAsync(
            _currentUser.RequireUserId(), ExtractBearerToken(),
            request ?? new ChangePasswordRequest(null, null), ct);
        return NoContent();
    }

    // ----- Notification settings (email toggle, Wave 2 §6.8) -----
    [HttpGet("notification-settings")]
    public async Task<ActionResult<NotificationSettingsDto>> GetNotificationSettings(CancellationToken ct)
        => Ok(await _notifications.GetSettingsAsync(ct));

    [HttpPut("notification-settings")]
    public async Task<ActionResult<NotificationSettingsDto>> UpdateNotificationSettings(
        [FromBody] UpdateNotificationSettingsRequest request, CancellationToken ct)
        => Ok(await _notifications.UpdateSettingsAsync(request ?? new UpdateNotificationSettingsRequest(null), ct));

    private string? ExtractBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var token = header[prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }
}
