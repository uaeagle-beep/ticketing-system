namespace TicketTracker.Application.Common;

/// <summary>
/// Thrown by Application services to signal a precise business outcome. The API layer's
/// exception middleware converts it into the uniform error envelope with the right HTTP
/// status (API_CONTRACT §2). Carries an optional per-field map for validation errors.
/// </summary>
public sealed class ServiceException : Exception
{
    public ServiceErrorCode Code { get; }

    /// <summary>Optional per-field validation messages (present for ValidationError).</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public ServiceException(ServiceErrorCode code, string message,
        IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Code = code;
        Errors = errors;
    }

    public static ServiceException Validation(string message,
        IReadOnlyDictionary<string, string[]>? errors = null)
        => new(ServiceErrorCode.ValidationError, message, errors);

    public static ServiceException Validation(string field, string fieldMessage)
        => new(ServiceErrorCode.ValidationError, fieldMessage,
            new Dictionary<string, string[]> { [field] = new[] { fieldMessage } });

    public static ServiceException NotFound(string message = "The requested resource was not found.")
        => new(ServiceErrorCode.NotFound, message);

    public static ServiceException Unauthorized(string message = "Authentication is required.")
        => new(ServiceErrorCode.Unauthorized, message);

    public static ServiceException Forbidden(string message = "You are not allowed to perform this action.")
        => new(ServiceErrorCode.Forbidden, message);
}
