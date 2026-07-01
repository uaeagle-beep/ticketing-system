using Microsoft.AspNetCore.Mvc;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// Team-scoped labels/tags CRUD (Wave 2, API_CONTRACT §5.6, ADR-0016). Member-managed: every endpoint
/// is M(team) — the service resolves the target team then requires access. All endpoints require auth.
/// Label assignment on a ticket lives on <see cref="TicketsController"/> (PUT /api/tickets/{id}/labels).
/// </summary>
[ApiController]
[Route("api/labels")]
public sealed class LabelsController : ControllerBase
{
    private readonly LabelService _labels;

    public LabelsController(LabelService labels) => _labels = labels;

    // ----- List a team's labels (§5.6) -----
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LabelDto>>> List([FromQuery] Guid? teamId, CancellationToken ct)
        => Ok(await _labels.ListAsync(teamId, ct));

    // ----- Create a label (§5.6) -----
    [HttpPost]
    public async Task<ActionResult<LabelDto>> Create([FromBody] CreateLabelRequest request, CancellationToken ct)
    {
        var label = await _labels.CreateAsync(request ?? new CreateLabelRequest(null, null, null), ct);
        return CreatedAtAction(nameof(List), new { teamId = label.TeamId }, label);
    }

    // ----- Rename / recolor a label (§5.6) -----
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LabelDto>> Update(Guid id, [FromBody] UpdateLabelRequest request, CancellationToken ct)
        => Ok(await _labels.UpdateAsync(id, request ?? new UpdateLabelRequest(null, null), ct));

    // ----- Delete a label (disposable; removes from all tickets, §5.6) -----
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _labels.DeleteAsync(id, ct);
        return NoContent();
    }
}
