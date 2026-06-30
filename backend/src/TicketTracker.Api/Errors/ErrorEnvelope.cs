using TicketTracker.Application.Common;

namespace TicketTracker.Api.Errors;

/// <summary>The uniform error envelope from API_CONTRACT §2: { "error": { code, message, errors? } }.</summary>
public sealed record ErrorEnvelope(ErrorBody Error);

public sealed record ErrorBody(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>
/// Maps a <see cref="ServiceErrorCode"/> to its HTTP status. The wire code string comes from
/// <see cref="ServiceErrorCodes.ToWire"/>. Together these implement the ADR-0006 taxonomy.
/// </summary>
public static class ErrorStatusMap
{
    public static int ToHttpStatus(ServiceErrorCode code) => code switch
    {
        ServiceErrorCode.ValidationError => StatusCodes.Status400BadRequest,
        ServiceErrorCode.EpicTeamMismatch => StatusCodes.Status400BadRequest,
        ServiceErrorCode.InvalidOrExpiredToken => StatusCodes.Status400BadRequest,
        ServiceErrorCode.Unauthorized => StatusCodes.Status401Unauthorized,
        ServiceErrorCode.InvalidCredentials => StatusCodes.Status401Unauthorized,
        ServiceErrorCode.AccountNotVerified => StatusCodes.Status403Forbidden,
        ServiceErrorCode.NotFound => StatusCodes.Status404NotFound,
        ServiceErrorCode.DuplicateTeamName => StatusCodes.Status409Conflict,
        ServiceErrorCode.TeamHasChildren => StatusCodes.Status409Conflict,
        ServiceErrorCode.EpicReferencedByTickets => StatusCodes.Status409Conflict,
        ServiceErrorCode.WipLimitReached => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };
}
