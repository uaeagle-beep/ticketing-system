using System.Text.Json;
using TicketTracker.Api.Auth;
using TicketTracker.Api.Errors;
using TicketTracker.Application.Common;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Middleware;

/// <summary>
/// Stateful opaque bearer-token authentication (ADR-0001, API_CONTRACT §1). For every
/// authenticated route it reads <c>Authorization: Bearer &lt;token&gt;</c>, resolves the session
/// to a verified user, and populates the scoped <see cref="CurrentUserAccessor"/>. Public routes
/// (auth endpoints + health + non-/api paths) pass through untouched. Any miss on a protected
/// route → 401 envelope. Tokens are never read from the URL.
/// </summary>
public sealed class BearerAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JsonSerializerOptions _json;

    // Public, no-auth endpoints (API_CONTRACT §1). Compared case-insensitively, exact path.
    private static readonly HashSet<string> PublicApiPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/signup",
        "/api/auth/login",
        "/api/auth/verify-email",
        "/api/auth/resend-verification",
        "/api/auth/forgot-password",
        "/api/auth/reset-password"
    };

    public BearerAuthMiddleware(RequestDelegate next, JsonSerializerOptions json)
    {
        _next = next;
        _json = json;
    }

    public async Task InvokeAsync(HttpContext context, AuthService authService, CurrentUserAccessor currentUser)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Only guard the business API surface. Health and static assets are public.
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || PublicApiPaths.Contains(path))
        {
            await _next(context);
            return;
        }

        var token = ExtractBearerToken(context);
        if (token is null)
        {
            await WriteUnauthorizedAsync(context);
            return;
        }

        var principal = await authService.ResolveSessionUserAsync(token, context.RequestAborted);
        if (principal is null)
        {
            await WriteUnauthorizedAsync(context);
            return;
        }

        currentUser.Set(principal.UserId, principal.IsAdmin, principal.TeamIds.ToHashSet());

        // Admin-zone gate (ADR-0007, R-3): a fast 403 for an authenticated non-admin hitting /api/admin/*.
        // This complements — and never replaces — the authoritative UserAdminService.RequireAdmin().
        if (path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase) && !principal.IsAdmin)
        {
            await WriteForbiddenAsync(context);
            return;
        }

        await _next(context);
    }

    private static string? ExtractBearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return null;

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var token = header[prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    private async Task WriteUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";
        var envelope = new ErrorEnvelope(new ErrorBody(
            ServiceErrorCodes.ToWire(ServiceErrorCode.Unauthorized),
            "Authentication is required."));
        await context.Response.WriteAsync(JsonSerializer.Serialize(envelope, _json));
    }

    private async Task WriteForbiddenAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";
        var envelope = new ErrorEnvelope(new ErrorBody(
            ServiceErrorCodes.ToWire(ServiceErrorCode.Forbidden),
            "Admin access required."));
        await context.Response.WriteAsync(JsonSerializer.Serialize(envelope, _json));
    }
}
