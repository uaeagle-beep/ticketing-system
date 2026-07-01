using System.Text.Json;
using TicketTracker.Api.Auth;
using TicketTracker.Api.Errors;
using TicketTracker.Application.Common;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Middleware;

/// <summary>
/// Stateful opaque bearer-token authentication (ADR-0001, API_CONTRACT §1) with the Wave-3 API-key path
/// (ADR-0021, §7.3). For every authenticated route it reads <c>Authorization: Bearer &lt;token&gt;</c>. A
/// <c>ptk_</c>-prefixed token is routed to <see cref="ApiKeyAuthenticator"/> and is accepted ONLY on
/// <c>/api/v1/*</c> (a key on any other path → 401, so a leaked key is never an admin/session credential);
/// everything else keeps the existing <see cref="AuthService.ResolveSessionUserAsync"/> session path. The
/// resolved principal populates the scoped <see cref="CurrentUserAccessor"/> (with scopes + the is-API-key
/// marker for the v1 scope gate). Public routes (auth endpoints + health + non-/api paths) pass through
/// untouched. Any miss on a protected route → 401 envelope. Tokens are never read from the URL.
/// </summary>
public sealed class BearerAuthMiddleware
{
    private const string ApiKeyPrefix = "ptk_";

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

    public async Task InvokeAsync(
        HttpContext context,
        AuthService authService,
        ApiKeyAuthenticator apiKeyAuthenticator,
        CurrentUserAccessor currentUser)
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

        var isV1 = path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase);

        // ----- API-key path (Wave 3, ADR-0021): a ptk_ token routes to the ApiKeyAuthenticator -----
        if (token.StartsWith(ApiKeyPrefix, StringComparison.Ordinal))
        {
            // A key is accepted ONLY on /api/v1/* — a ptk_ token on any other path is 401, so a leaked key
            // can never reach a session-only or admin surface (least privilege, §7.3).
            if (!isV1)
            {
                await WriteUnauthorizedAsync(context);
                return;
            }

            var keyPrincipal = await apiKeyAuthenticator.ResolveAsync(token, context.RequestAborted);
            if (keyPrincipal is null)
            {
                await WriteUnauthorizedAsync(context);
                return;
            }

            currentUser.SetApiKey(
                keyPrincipal.UserId, keyPrincipal.IsAdmin, keyPrincipal.TeamIds.ToHashSet(), keyPrincipal.Scopes);
            await _next(context);
            return;
        }

        // ----- Session path (ADR-0001): the existing opaque-bearer resolution -----
        // A session bearer is NOT accepted on the public /api/v1 surface (it is the key-only surface, §5.6).
        if (isV1)
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
