namespace TicketTracker.Application.Dtos;

// ----- Auth requests (API_CONTRACT §3) -----

public sealed record SignupRequest(string? Email, string? Password);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record VerifyEmailRequest(string? Token);

public sealed record ResendVerificationRequest(string? Email);

// ----- Self-service account requests (Wave 1: F-01 reset, F-04 profile) -----

/// <summary>POST /api/auth/forgot-password (F-01, public, non-enumerating).</summary>
public sealed record ForgotPasswordRequest(string? Email);

/// <summary>POST /api/auth/reset-password (F-01, public). Consumes a token + sets a new password.</summary>
public sealed record ResetPasswordRequest(string? Token, string? Password);

/// <summary>PUT /api/me/profile (F-04, self). Set or clear the display name (null/blank ⇒ clear).</summary>
public sealed record UpdateProfileRequest(string? Name);

/// <summary>POST /api/me/password (F-04, self). Current-password re-auth + new password.</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

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
