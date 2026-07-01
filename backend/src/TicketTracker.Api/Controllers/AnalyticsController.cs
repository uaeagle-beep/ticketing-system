using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// Reporting dashboard (Wave 3, ADR-0020, API_CONTRACT §5.4). One composite, read-only, team-scoped endpoint
/// returning every dashboard metric in a single round-trip. M(team): the service resolves the team then
/// requires access (404-then-403 anti-IDOR; admin sees any). Session auth only — analytics is a UI concern,
/// not an API-key surface in v1. Requires a valid, verified, non-blocked session (BearerAuthMiddleware).
/// </summary>
[ApiController]
[Route("api/analytics")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _analytics;

    public AnalyticsController(AnalyticsService analytics) => _analytics = analytics;

    // ----- Composite dashboard (§5.4) — M(team) -----
    // from/to are optional YYYY-MM-DD UTC calendar days; parsed strictly here so a bad date is a uniform
    // 400 validation_error (like the board's strict enum parsing) rather than a model-binding failure.
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardDto>> Dashboard(
        [FromQuery] Guid? teamId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var fromDate = ParseDate(from, "from");
        var toDate = ParseDate(to, "to");
        return Ok(await _analytics.GetDashboardAsync(teamId, fromDate, toDate, ct));
    }

    /// <summary>Strictly parse an optional YYYY-MM-DD calendar day; a malformed value is a 400 keyed to the field.</summary>
    private static DateOnly? ParseDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            throw ServiceException.Validation(field, $"'{field}' must be a valid date in YYYY-MM-DD format.");
        return parsed;
    }
}
