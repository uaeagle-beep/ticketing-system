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
    private readonly WatchService _watch;
    private readonly ActivityService _activity;

    public TicketsController(TicketService tickets, CommentService comments, WatchService watch, ActivityService activity)
    {
        _tickets = tickets;
        _comments = comments;
        _watch = watch;
        _activity = activity;
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
        [FromQuery] Guid? labelId,
        CancellationToken ct)
        => Ok(await _tickets.GetBoardAsync(
            teamId, type, epicId, search, priority, assigneeId, assignedToMe, dueFilter, labelId, ct));

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

    // ----- Labels full-set replace (Wave 2, §5.7, ADR-0016) — M(team of ticket) -----
    [HttpPut("{id:guid}/labels")]
    public async Task<ActionResult<TicketDetailDto>> SetLabels(Guid id, [FromBody] SetLabelsRequest request, CancellationToken ct)
        => Ok(await _tickets.SetLabelsAsync(id, request ?? new SetLabelsRequest(null), ct));

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

    // ----- Watchers (Wave 2, §5.4) — M(team of ticket) -----
    [HttpGet("{id:guid}/watchers")]
    public async Task<ActionResult<WatchersDto>> Watchers(Guid id, CancellationToken ct)
        => Ok(await _watch.ListWatchersAsync(id, ct));

    [HttpPost("{id:guid}/watch")]
    public async Task<ActionResult<WatchStatusDto>> Watch(Guid id, CancellationToken ct)
        => Ok(await _watch.WatchAsync(id, ct));

    [HttpDelete("{id:guid}/watch")]
    public async Task<ActionResult<WatchStatusDto>> Unwatch(Guid id, CancellationToken ct)
        => Ok(await _watch.UnwatchAsync(id, ct));

    // ----- Activity timeline (Wave 2, §5.5) — M(team of ticket) -----
    [HttpGet("{id:guid}/activity")]
    public async Task<ActionResult<ActivityListDto>> Activity(
        Guid id, [FromQuery] int? limit, [FromQuery] string? cursor, CancellationToken ct)
        => Ok(await _activity.ListAsync(id, limit, cursor, ct));
}
