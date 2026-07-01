using Microsoft.AspNetCore.Mvc;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// Tickets + board read + drag-and-drop state + comments sub-resource (API_CONTRACT §6, §7).
/// All endpoints require auth.
/// </summary>
[ApiController]
[Route("api/tickets")]
public sealed class TicketsController : ControllerBase
{
    private readonly TicketService _tickets;
    private readonly CommentService _comments;

    public TicketsController(TicketService tickets, CommentService comments)
    {
        _tickets = tickets;
        _comments = comments;
    }

    // ----- Board / list (§6.1) -----
    [HttpGet]
    public async Task<ActionResult<BoardDto>> Board(
        [FromQuery] Guid? teamId,
        [FromQuery] string? type,
        [FromQuery] Guid? epicId,
        [FromQuery] string? search,
        [FromQuery] string? priority,
        [FromQuery] Guid? assigneeId,
        [FromQuery] bool assignedToMe,
        [FromQuery] string? dueFilter,
        CancellationToken ct)
        => Ok(await _tickets.GetBoardAsync(
            teamId, type, epicId, search, priority, assigneeId, assignedToMe, dueFilter, ct));

    // ----- Detail (§6.2) -----
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _tickets.GetByIdAsync(id, ct));

    // ----- Create (§6.3) -----
    [HttpPost]
    public async Task<ActionResult<TicketDetailDto>> Create([FromBody] CreateTicketRequest request, CancellationToken ct)
    {
        var ticket = await _tickets.CreateAsync(
            request ?? new CreateTicketRequest(null, null, null, null, null, null), ct);
        return CreatedAtAction(nameof(Get), new { id = ticket.Id }, ticket);
    }

    // ----- Edit (§6.4) -----
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TicketDetailDto>> Update(Guid id, [FromBody] UpdateTicketRequest request, CancellationToken ct)
        => Ok(await _tickets.UpdateAsync(id,
            request ?? new UpdateTicketRequest(null, null, null, null, null, null), ct));

    // ----- Drag-and-drop state (§6.5) -----
    [HttpPatch("{id:guid}/state")]
    public async Task<ActionResult<TicketStateDto>> PatchState(Guid id, [FromBody] PatchTicketStateRequest request, CancellationToken ct)
        => Ok(await _tickets.PatchStateAsync(id, request ?? new PatchTicketStateRequest(null), ct));

    // ----- Assignees full-set replace (§4.2) -----
    [HttpPut("{id:guid}/assignees")]
    public async Task<ActionResult<TicketDetailDto>> SetAssignees(Guid id, [FromBody] SetAssigneesRequest request, CancellationToken ct)
        => Ok(await _tickets.SetAssigneesAsync(id, request ?? new SetAssigneesRequest(null), ct));

    // ----- Delete (§6.6) -----
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _tickets.DeleteAsync(id, ct);
        return NoContent();
    }

    // ----- Comments sub-resource (§7) -----
    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<IReadOnlyList<CommentDto>>> ListComments(Guid id, CancellationToken ct)
        => Ok(await _comments.ListAsync(id, ct));

    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<CommentDto>> AddComment(Guid id, [FromBody] CreateCommentRequest request, CancellationToken ct)
    {
        var comment = await _comments.AddAsync(id, request ?? new CreateCommentRequest(null), ct);
        return StatusCode(StatusCodes.Status201Created, comment);
    }
}
