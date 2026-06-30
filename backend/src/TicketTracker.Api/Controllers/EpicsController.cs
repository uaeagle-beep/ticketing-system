using Microsoft.AspNetCore.Mvc;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>Epics CRUD (API_CONTRACT §5). All endpoints require auth.</summary>
[ApiController]
[Route("api/epics")]
public sealed class EpicsController : ControllerBase
{
    private readonly EpicService _epics;

    public EpicsController(EpicService epics) => _epics = epics;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EpicDto>>> List([FromQuery] Guid? teamId, CancellationToken ct)
        => Ok(await _epics.ListByTeamAsync(teamId, ct));

    [HttpPost]
    public async Task<ActionResult<EpicDto>> Create([FromBody] CreateEpicRequest request, CancellationToken ct)
    {
        var epic = await _epics.CreateAsync(request ?? new CreateEpicRequest(null, null, null), ct);
        return CreatedAtAction(nameof(List), new { teamId = epic.TeamId }, epic);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EpicDto>> Update(Guid id, [FromBody] UpdateEpicRequest request, CancellationToken ct)
        => Ok(await _epics.UpdateAsync(id, request ?? new UpdateEpicRequest(null, null), ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _epics.DeleteAsync(id, ct);
        return NoContent();
    }
}
