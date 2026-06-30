namespace TicketTracker.Application.Dtos;

// ----- Auth requests (API_CONTRACT §3) -----

public sealed record SignupRequest(string? Email, string? Password);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record VerifyEmailRequest(string? Token);

public sealed record ResendVerificationRequest(string? Email);

// ----- Auth responses -----

public sealed record MessageResponse(string Message);

public sealed record UserDto(Guid Id, string Email, bool EmailVerified);

public sealed record LoginResponse(string Token, UserDto User, DateTime ExpiresAt);
