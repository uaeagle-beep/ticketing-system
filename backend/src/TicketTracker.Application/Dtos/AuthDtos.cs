namespace TicketTracker.Application.Dtos;

// ----- Auth requests (API_CONTRACT §3) -----

public sealed record SignupRequest(string? Email, string? Password);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record VerifyEmailRequest(string? Token);

public sealed record ResendVerificationRequest(string? Email);

// ----- Auth responses -----

public sealed record MessageResponse(string Message);

/// <summary>A lightweight team reference (id + name) used inside user payloads (ADR-0007).</summary>
public sealed record TeamRefDto(Guid Id, string Name);

/// <summary>
/// The authenticated user as returned by /api/auth/me and LoginResponse.user. Carries the
/// authorization context (<c>IsAdmin</c>, <c>IsBlocked</c>, <c>Teams</c>) that drives the SPA's
/// nav/team-selector and the "load last/first team" client logic (ADR-0007, §4.9). <c>Name</c> is
/// the optional display name (null ⇒ the SPA shows <c>Email</c>); email stays the login/account key.
/// </summary>
public sealed record UserDto(
    Guid Id, string Email, string? Name, bool EmailVerified, bool IsAdmin, bool IsBlocked,
    IReadOnlyList<TeamRefDto> Teams);

public sealed record LoginResponse(string Token, UserDto User, DateTime ExpiresAt);
