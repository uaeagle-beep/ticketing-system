using Microsoft.AspNetCore.Mvc;
using TicketTracker.Api.Auth;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// The public, API-key-authenticated v1 surface (Wave 3, ADR-0021, API_CONTRACT §5.6, [ASSUMPTION
/// W3-APIKEY-SCOPE]). Thin controllers that REUSE the exact same <see cref="TicketService"/> /
/// <see cref="CommentService"/> as the session UI — same validation, same team-scoped authz (the key's
/// owner's live memberships + admin flag populate <c>ICurrentUser</c>), same DTOs (minus isWatching). The
/// ONLY differences vs the session routes: (1) auth is by API key (BearerAuthMiddleware accepts ptk_ only on
/// /api/v1/*); (2) a scope gate runs after auth — <c>tickets:read</c> for GET, <c>tickets:write</c> for
/// mutating; insufficient → 403 <c>insufficient_scope</c>. No delete, no admin, no attachment transfer here.
/// </summary>
[ApiController]
[Route("api/v1/tickets")]
public sealed class V1TicketsController : ControllerBase
{
    private readonly TicketService _tickets;
    private readonly CommentService _comments;
    private readonly CurrentUserAccessor _currentUser;

    public V1TicketsController(TicketService tickets, CommentService comments, CurrentUserAccessor currentUser)
    {
        _tickets = tickets;
        _comments = comments;
        _currentUser = currentUser;
    }

    // ----- Board / list (read) -----
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
    {
        _currentUser.RequireScope(ApiKeyScopeCanonical.TicketsRead);
        return Ok(await _tickets.GetBoardAsync(
            teamId, type, epicId, search, priority, assigneeId, assignedToMe, dueFilter, labelId, ct));
    }

    // ----- Detail (read) -----
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDetailDto>> Get(Guid id, CancellationToken ct)
    {
        _currentUser.RequireScope(ApiKeyScopeCanonical.TicketsRead);
        return Ok(await _tickets.GetByIdAsync(id, ct));
    }

    // ----- Create (write) -----
    [HttpPost]
    public async Task<ActionResult<TicketDetailDto>> Create([FromBody] CreateTicketRequest request, CancellationToken ct)
    {
        _currentUser.RequireScope(ApiKeyScopeCanonical.TicketsWrite);
        var ticket = await _tickets.CreateAsync(
            request ?? new CreateTicketRequest(null, null, null, null, null, null), ct);
        return StatusCode(StatusCodes.Status201Created, ticket);
    }

    // ----- Edit (write) -----
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TicketDetailDto>> Update(Guid id, [FromBody] UpdateTicketRequest request, CancellationToken ct)
    {
        _currentUser.RequireScope(ApiKeyScopeCanonical.TicketsWrite);
        return Ok(await _tickets.UpdateAsync(id,
            request ?? new UpdateTicketRequest(null, null, null, null, null, null), ct));
    }

    // ----- Patch state (write) -----
    [HttpPatch("{id:guid}/state")]
    public async Task<ActionResult<TicketStateDto>> PatchState(Guid id, [FromBody] PatchTicketStateRequest request, CancellationToken ct)
    {
        _currentUser.RequireScope(ApiKeyScopeCanonical.TicketsWrite);
        return Ok(await _tickets.PatchStateAsync(id, request ?? new PatchTicketStateRequest(null), ct));
    }

    // ----- Comments sub-resource (read/write) -----
    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<IReadOnlyList<CommentDto>>> ListComments(Guid id, CancellationToken ct)
    {
        _currentUser.RequireScope(ApiKeyScopeCanonical.TicketsRead);
        return Ok(await _comments.ListAsync(id, ct));
    }

    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<CommentDto>> AddComment(Guid id, [FromBody] CreateCommentRequest request, CancellationToken ct)
    {
        _currentUser.RequireScope(ApiKeyScopeCanonical.TicketsWrite);
        var comment = await _comments.AddAsync(id, request ?? new CreateCommentRequest(null), ct);
        return StatusCode(StatusCodes.Status201Created, comment);
    }
}
