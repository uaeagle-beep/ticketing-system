using Microsoft.AspNetCore.Mvc;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>Teams CRUD (API_CONTRACT §4). All endpoints require auth.</summary>
[ApiController]
[Route("api/teams")]
public sealed class TeamsController : ControllerBase
{
    private readonly TeamService _teams;

    public TeamsController(TeamService teams) => _teams = teams;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TeamDto>>> List(CancellationToken ct)
        => Ok(await _teams.ListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<TeamDto>> Create([FromBody] CreateTeamRequest request, CancellationToken ct)
    {
        var team = await _teams.CreateAsync(request ?? new CreateTeamRequest(null), ct);
        return CreatedAtAction(nameof(List), new { id = team.Id }, team);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TeamDto>> Rename(Guid id, [FromBody] UpdateTeamRequest request, CancellationToken ct)
        => Ok(await _teams.RenameAsync(id, request ?? new UpdateTeamRequest(null), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _teams.DeleteAsync(id, ct);
        return NoContent();
    }
}
