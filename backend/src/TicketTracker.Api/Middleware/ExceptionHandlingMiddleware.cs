using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Api.Errors;
using TicketTracker.Application.Common;

namespace TicketTracker.Api.Middleware;

/// <summary>
/// Global exception → uniform error envelope mapper (ARCHITECTURE §3.2, API_CONTRACT §2).
/// Translates <see cref="ServiceException"/> to its HTTP status + code, maps a residual FK
/// RESTRICT violation (the delete-guard backstop) to 409, and everything else to 500 without
/// leaking internals.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly JsonSerializerOptions _json;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        JsonSerializerOptions json)
    {
        _next = next;
        _logger = logger;
        _json = json;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ServiceException ex)
        {
            await WriteAsync(context, ErrorStatusMap.ToHttpStatus(ex.Code),
                ServiceErrorCodes.ToWire(ex.Code), ex.Message, ex.Errors);
        }
        catch (DbUpdateException ex)
        {
            // FK RESTRICT backstop (EC7): a delete that the service guard missed still cannot
            // orphan data. Surface a clean 409 rather than a raw provider error.
            _logger.LogWarning(ex, "Database update failed; mapped to 409 conflict.");
            await WriteAsync(context, StatusCodes.Status409Conflict, "conflict",
                "The operation conflicts with the current state of the data.", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing {Method} {Path}.",
                context.Request.Method, context.Request.Path);
            await WriteAsync(context, StatusCodes.Status500InternalServerError, "internal_error",
                "An unexpected error occurred.", null);
        }
    }

    private async Task WriteAsync(HttpContext context, int status, string code, string message,
        IReadOnlyDictionary<string, string[]>? errors)
    {
        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response already started; cannot write error envelope for {Code}.", code);
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";

        var envelope = new ErrorEnvelope(new ErrorBody(code, message, errors));
        await context.Response.WriteAsync(JsonSerializer.Serialize(envelope, _json));
    }
}
