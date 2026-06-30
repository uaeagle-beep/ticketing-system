using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Api.HostedServices;
using TicketTracker.Infrastructure.Persistence;

namespace TicketTracker.Api.Controllers;

/// <summary>Public health endpoints (API_CONTRACT §8). No auth.</summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "live" });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(
        [FromServices] DatabaseReadinessState readiness,
        [FromServices] AppDbContext db,
        CancellationToken ct)
    {
        bool dbReachable;
        try
        {
            dbReachable = await db.Database.CanConnectAsync(ct);
        }
        catch
        {
            dbReachable = false;
        }

        if (!dbReachable)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { status = "not-ready", reason = "database" });

        if (!readiness.MigrationsApplied)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { status = "not-ready", reason = "migrations" });

        return Ok(new { status = "ready" });
    }
}
