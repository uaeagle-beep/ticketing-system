namespace TicketTracker.Application.Options;

/// <summary>
/// Auth/verification lifetimes and the verification-link base URL. Bound from environment
/// (ARCHITECTURE §8). All consumed by the Application layer (AuthService).
/// </summary>
public sealed class AuthOptions
{
    /// <summary>Verification token lifetime in hours (env TOKEN_TTL_HOURS, default 24, source §3).</summary>
    public int TokenTtlHours { get; set; } = 24;

    /// <summary>Session bearer-token lifetime in hours (env SESSION_TTL_HOURS, default 72, ADR-0001).</summary>
    public int SessionTtlHours { get; set; } = 72;

    /// <summary>Base URL for verification links (env FRONTEND_URL). Link = {FrontendUrl}/verify-email?token=RAW.</summary>
    public string FrontendUrl { get; set; } = "http://localhost:8080";
}
