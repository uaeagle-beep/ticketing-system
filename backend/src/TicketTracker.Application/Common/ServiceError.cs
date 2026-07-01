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

    /// <summary>403 — authenticated but not allowed: non-admin in admin zone, or member acting on a non-member team's resource (ADR-0007).</summary>
    Forbidden,

    /// <summary>401 — login (or session resolution) for a blocked account (ADR-0007, ASR-2).</summary>
    AccountBlocked,

    /// <summary>409 — a demote/block/delete that would leave zero active admins (ADR-0008, INV-2).</summary>
    LastAdminRequired,

    /// <summary>409 — admin create-user with an email that already exists (admin zone, enumeration acceptable).</summary>
    EmailInUse,

    /// <summary>404 — resource addressed in the URL path does not exist.</summary>
    NotFound,

    /// <summary>409 — team create/rename collides case-insensitively.</summary>
    DuplicateTeamName,

    /// <summary>409 — label create/rename collides case-insensitively within the team (ADR-0016).</summary>
    DuplicateLabelName,

    /// <summary>409 — delete team that has tickets or epics.</summary>
    TeamHasChildren,

    /// <summary>409 — delete epic referenced by >= 1 ticket.</summary>
    EpicReferencedByTickets,

    /// <summary>409 — target (team, state) is at its WIP limit and the ticket isn't already in it.</summary>
    WipLimitReached,

    /// <summary>400 — verify-email: token unknown, consumed, or expired.</summary>
    InvalidOrExpiredToken,

    /// <summary>413 — attachment upload exceeds ATTACHMENTS_MAX_BYTES (Wave 3, ADR-0018).</summary>
    PayloadTooLarge,

    /// <summary>415 — attachment content-type not in the allowlist, declared or sniffed (Wave 3, ADR-0018).</summary>
    UnsupportedMediaType,

    /// <summary>403 — the API key lacks the scope required for the requested /api/v1 route (Wave 3, ADR-0021).</summary>
    InsufficientScope
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
        ServiceErrorCode.Forbidden => "forbidden",
        ServiceErrorCode.AccountBlocked => "account_blocked",
        ServiceErrorCode.LastAdminRequired => "last_admin_required",
        ServiceErrorCode.EmailInUse => "email_in_use",
        ServiceErrorCode.NotFound => "not_found",
        ServiceErrorCode.DuplicateTeamName => "duplicate_team_name",
        ServiceErrorCode.DuplicateLabelName => "duplicate_label_name",
        ServiceErrorCode.TeamHasChildren => "team_has_children",
        ServiceErrorCode.EpicReferencedByTickets => "epic_referenced_by_tickets",
        ServiceErrorCode.WipLimitReached => "wip_limit_reached",
        ServiceErrorCode.InvalidOrExpiredToken => "invalid_or_expired_token",
        ServiceErrorCode.PayloadTooLarge => "payload_too_large",
        ServiceErrorCode.UnsupportedMediaType => "unsupported_media_type",
        ServiceErrorCode.InsufficientScope => "insufficient_scope",
        _ => "error"
    };
}
