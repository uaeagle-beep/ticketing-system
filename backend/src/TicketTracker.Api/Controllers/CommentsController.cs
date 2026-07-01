using Microsoft.AspNetCore.Mvc;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// Top-level comment resource for edit/delete-own (F-12, API_CONTRACT §7). A comment id is globally
/// unique and the author check is the primary gate, so these live at <c>/api/comments/{id}</c> rather
/// than under the ticket (WAVE2 §5.2, ADR-0015). Listing and adding comments remain a ticket
/// sub-resource on <see cref="TicketsController"/>. All endpoints require auth.
/// </summary>
[ApiController]
[Route("api/comments")]
public sealed class CommentsController : ControllerBase
{
    private readonly CommentService _comments;

    public CommentsController(CommentService comments) => _comments = comments;

    // ----- Edit own comment (author-only, F-12) -----
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CommentDto>> Edit(Guid id, [FromBody] EditCommentRequest request, CancellationToken ct)
        => Ok(await _comments.EditAsync(id, request ?? new EditCommentRequest(null), ct));

    // ----- Delete comment (author or admin, F-12) -----
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _comments.DeleteAsync(id, ct);
        return NoContent();
    }
}
