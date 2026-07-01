using Microsoft.AspNetCore.Mvc;
using TicketTracker.Api.Auth;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// Auth endpoints (API_CONTRACT §3). signup/login/verify-email/resend-verification are public;
/// logout and me require a valid bearer token (enforced by <c>BearerAuthMiddleware</c>).
/// Controllers are thin: they map DTO ↔ service calls and set status codes only.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth) => _auth = auth;

    [HttpPost("signup")]
    public async Task<IActionResult> Signup([FromBody] SignupRequest request, CancellationToken ct)
    {
        var result = await _auth.SignupAsync(request ?? new SignupRequest(null, null), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request ?? new LoginRequest(null, null), ct);
        return Ok(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _auth.LogoutAsync(ExtractBearerToken(), ct);
        return NoContent();
    }

    [HttpPost("verify-email")]
    public async Task<ActionResult<MessageResponse>> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
    {
        var result = await _auth.VerifyEmailAsync(request ?? new VerifyEmailRequest(null), ct);
        return Ok(result);
    }

    [HttpPost("resend-verification")]
    public async Task<ActionResult<MessageResponse>> Resend([FromBody] ResendVerificationRequest request, CancellationToken ct)
    {
        var result = await _auth.ResendVerificationAsync(request ?? new ResendVerificationRequest(null), ct);
        return Accepted(value: result);
    }

    // ----- Self-service password reset (public, non-enumerating, F-01) -----

    [HttpPost("forgot-password")]
    public async Task<ActionResult<MessageResponse>> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var result = await _auth.RequestPasswordResetAsync(request ?? new ForgotPasswordRequest(null), ct);
        return Accepted(value: result);
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult<MessageResponse>> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await _auth.ResetPasswordAsync(request ?? new ResetPasswordRequest(null, null), ct);
        return Ok(result);
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me([FromServices] CurrentUserAccessor currentUser, CancellationToken ct)
    {
        var result = await _auth.GetMeAsync(currentUser.RequireUserId(), ct);
        return Ok(result);
    }

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
