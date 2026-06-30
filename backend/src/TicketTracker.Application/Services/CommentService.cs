using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Validation;
using TicketTracker.Domain.Entities;

namespace TicketTracker.Application.Services;

/// <summary>
/// Comments (E5). Body non-empty after trim (V23); author + created_at server-set (V23,
/// A20/A21). Listing is oldest-first (V23, FR-E5-4). Adding a comment NEVER touches the
/// ticket's modified_at (V21) — this path simply does not write to the ticket.
/// </summary>
public sealed class CommentService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public CommentService(IAppDbContext db, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<CommentDto>> ListAsync(Guid ticketId, CancellationToken ct)
    {
        // Resolve the ticket's team (404 if the ticket is absent) then check access (403) — §3.3.
        var teamId = await ResolveTicketTeamAsync(ticketId, ct);
        _currentUser.RequireTeamAccess(teamId);

        return await _db.Comments.AsNoTracking()
            .Where(c => c.TicketId == ticketId)
            .OrderBy(c => c.CreatedAt)           // oldest first (V23)
            .ThenBy(c => c.Id)                   // stable tiebreaker for identical timestamps
            .Select(c => new CommentDto(
                c.Id,
                c.TicketId,
                c.AuthorId,
                c.Author != null ? c.Author.Email : string.Empty,
                c.Body,
                c.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<CommentDto> AddAsync(Guid ticketId, CreateCommentRequest request, CancellationToken ct)
    {
        // Resolve the ticket's team (404 if the ticket is absent) then check access (403) — §3.3.
        var teamId = await ResolveTicketTeamAsync(ticketId, ct);
        _currentUser.RequireTeamAccess(teamId);

        var body = Normalization.Trim(request.Body);
        if (Normalization.IsBlank(body))
            throw ServiceException.Validation("body", "Comment body is required.");
        if (body.Length > FieldLimits.CommentBodyMax)
            throw ServiceException.Validation("body", $"Comment body must be at most {FieldLimits.CommentBodyMax} characters.");

        var authorId = _currentUser.RequireUserId();
        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            AuthorId = authorId,
            Body = body,
            CreatedAt = _clock.UtcNow
        };
        _db.Comments.Add(comment);
        await _db.SaveChangesAsync(ct);   // does NOT touch ticket.modified_at (V21)

        var authorEmail = await _db.Users.AsNoTracking()
            .Where(u => u.Id == authorId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return new CommentDto(comment.Id, comment.TicketId, comment.AuthorId, authorEmail, comment.Body, comment.CreatedAt);
    }

    /// <summary>
    /// Resolves the team that owns the given ticket. Throws 404 not_found if the ticket does not
    /// exist (so the 404-then-403 ordering of §3.3 holds for the comments sub-resource).
    /// </summary>
    private async Task<Guid> ResolveTicketTeamAsync(Guid ticketId, CancellationToken ct)
    {
        var teamId = await _db.Tickets.AsNoTracking()
            .Where(t => t.Id == ticketId)
            .Select(t => (Guid?)t.TeamId)
            .FirstOrDefaultAsync(ct);
        return teamId ?? throw ServiceException.NotFound("Ticket not found.");
    }
}
