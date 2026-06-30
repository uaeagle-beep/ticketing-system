namespace TicketTracker.Application.Common;

/// <summary>
/// The stable machine error codes from API_CONTRACT §2 / ADR-0006. The API layer maps
/// each to its HTTP status + envelope. Keeping this in the Application layer lets services
/// signal precise outcomes without depending on ASP.NET / HTTP.
/// </summary>
public enum ServiceErrorCode
{
    /// <summary>400 — malformed/invalid payload, bad enum, or a non-existent reference in the body.</summary>
    ValidationError,

    /// <summary>400 — ticket epicId belongs to a team other than the ticket's teamId.</summary>
    EpicTeamMismatch,

    /// <summary>401 — no/invalid/expired/logged-out bearer token.</summary>
    Unauthorized,

    /// <summary>401 — login: wrong password or unknown email (identical message).</summary>
    InvalidCredentials,

    /// <summary>403 — login with correct creds on an unverified account.</summary>
    AccountNotVerified,

    /// <summary>404 — resource addressed in the URL path does not exist.</summary>
    NotFound,

    /// <summary>409 — team create/rename collides case-insensitively.</summary>
    DuplicateTeamName,

    /// <summary>409 — delete team that has tickets or epics.</summary>
    TeamHasChildren,

    /// <summary>409 — delete epic referenced by >= 1 ticket.</summary>
    EpicReferencedByTickets,

    /// <summary>400 — verify-email: token unknown, consumed, or expired.</summary>
    InvalidOrExpiredToken
}

/// <summary>
/// Maps a <see cref="ServiceErrorCode"/> to the exact machine string used in the
/// error envelope (API_CONTRACT §2). Kept separate from the HTTP-status mapping,
/// which lives in the API layer.
/// </summary>
public static class ServiceErrorCodes
{
    public static string ToWire(ServiceErrorCode code) => code switch
    {
        ServiceErrorCode.ValidationError => "validation_error",
        ServiceErrorCode.EpicTeamMismatch => "epic_team_mismatch",
        ServiceErrorCode.Unauthorized => "unauthorized",
        ServiceErrorCode.InvalidCredentials => "invalid_credentials",
        ServiceErrorCode.AccountNotVerified => "account_not_verified",
        ServiceErrorCode.NotFound => "not_found",
        ServiceErrorCode.DuplicateTeamName => "duplicate_team_name",
        ServiceErrorCode.TeamHasChildren => "team_has_children",
        ServiceErrorCode.EpicReferencedByTickets => "epic_referenced_by_tickets",
        ServiceErrorCode.InvalidOrExpiredToken => "invalid_or_expired_token",
        _ => "error"
    };
}
