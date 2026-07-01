using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Events;
using TicketTracker.Application.Validation;
using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Services;

/// <summary>
/// Comments (E5). Body non-empty after trim (V23); author + created_at server-set (V23,
/// A20/A21). Listing is oldest-first (V23, FR-E5-4). Adding a comment NEVER touches the
/// ticket's modified_at (V21) — this path simply does not write to the ticket.
/// <para>
/// Wave 2 (F-12, ADR-0015): an author may edit their own comment (author-only, no admin override)
/// and an author-or-admin may delete a comment. Both resolve the comment → its ticket's team and
/// check <see cref="ICurrentUser.RequireTeamAccess"/> BEFORE the author/role gate (404-then-403,
/// ADR-0007 §3.3). An edit stamps <c>edited_at</c>; a no-op edit (same normalized body) does not.
/// Neither raises events in Phase 1 — the event backbone arrives in Phase 2 (WAVE2 §12).
/// </para>
/// </summary>
public sealed class CommentService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IDomainEventPublisher _publisher;

    public CommentService(IAppDbContext db, IClock clock, ICurrentUser currentUser, IDomainEventPublisher publisher)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _publisher = publisher;
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
                c.Author != null ? c.Author.Name : null,
                c.Body,
                c.CreatedAt,
                c.EditedAt != null,
                c.EditedAt))
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
        var now = _clock.UtcNow;
        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            AuthorId = authorId,
            Body = body,
            CreatedAt = now
        };
        _db.Comments.Add(comment);

        // Auto-watch the commenter (§6.3), inside the same save as the mutation.
        await WatchService.AddWatch(_db, ticketId, authorId, now, ct);

        await _db.SaveChangesAsync(ct);   // does NOT touch ticket.modified_at (V21)

        var author = await _db.Users.AsNoTracking()
            .Where(u => u.Id == authorId)
            .Select(u => new { u.Email, u.Name })
            .FirstOrDefaultAsync(ct);
        var authorEmail = author?.Email ?? string.Empty;
        var authorName = author?.Name;

        // Publish AFTER commit (§6.2): comment_added → activity + notification (email included). The
        // comment author is the actor and is never notified about their own comment.
        var actor = DisplayName(authorName, authorEmail);
        var summary = EventSummaries.CommentAdded(actor);
        await _publisher.PublishAsync(new[]
        {
            new TicketEvent(EventType.CommentAdded, ticketId, authorId, comment.Id, null, summary, summary)
        }, ct);

        return new CommentDto(comment.Id, comment.TicketId, comment.AuthorId, authorEmail, authorName, comment.Body, comment.CreatedAt);
    }

    /// <summary>
    /// Edit a comment (F-12, WAVE2 §5.2). Author-only — even an admin may not edit another user's
    /// words (ADR-0015). Ordering (anti-IDOR §3.3): resolve the comment → 404; resolve its ticket's
    /// team → <see cref="ICurrentUser.RequireTeamAccess"/> → 403; then require the caller is the
    /// author → 403. On a real body change, sets <c>edited_at = now</c>; a no-op edit (same normalized
    /// body) persists nothing and returns the unchanged comment (mirrors the modified_at no-op rule).
    /// </summary>
    public async Task<CommentDto> EditAsync(Guid commentId, EditCommentRequest request, CancellationToken ct)
    {
        var comment = await _db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, ct)
            ?? throw ServiceException.NotFound("Comment not found.");

        // Resolve the ticket's team then check access (403 if the caller can't even see the ticket).
        var teamId = await ResolveTicketTeamAsync(comment.TicketId, ct);
        _currentUser.RequireTeamAccess(teamId);

        // Author-only — no admin override on edit (ADR-0015).
        var currentUserId = _currentUser.RequireUserId();
        if (comment.AuthorId != currentUserId)
            throw ServiceException.Forbidden("You can only edit your own comments.");

        var body = Normalization.Trim(request.Body);
        if (Normalization.IsBlank(body))
            throw ServiceException.Validation("body", "Comment body is required.");
        if (body.Length > FieldLimits.CommentBodyMax)
            throw ServiceException.Validation("body", $"Comment body must be at most {FieldLimits.CommentBodyMax} characters.");

        var author = await _db.Users.AsNoTracking()
            .Where(u => u.Id == comment.AuthorId)
            .Select(u => new { u.Email, u.Name })
            .FirstOrDefaultAsync(ct);

        // No-op rule: unchanged body persists nothing, does not advance edited_at, and raises NO event.
        if (body != comment.Body)
        {
            comment.Body = body;
            comment.EditedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);

            // Publish AFTER commit (§6.2): comment_edited → activity ONLY (no notification/email, ADR-0015).
            var actor = DisplayName(author?.Name, author?.Email ?? string.Empty);
            var summary = EventSummaries.CommentEdited(actor);
            await _publisher.PublishAsync(new[]
            {
                new TicketEvent(EventType.CommentEdited, comment.TicketId, currentUserId, comment.Id, null, summary, summary)
            }, ct);
        }

        return new CommentDto(comment.Id, comment.TicketId, comment.AuthorId,
            author?.Email ?? string.Empty, author?.Name, comment.Body, comment.CreatedAt,
            comment.EditedAt != null, comment.EditedAt);
    }

    /// <summary>
    /// Delete a comment (F-12, WAVE2 §5.2). Author OR admin (moderation override, ADR-0015). Same
    /// resolve-then-check ordering as <see cref="EditAsync"/>: 404 → team-access 403 → author/admin
    /// gate 403. Hard-deletes the row (Ticket→Comment cascade is unaffected; there are no other
    /// references to null in Phase 1).
    /// </summary>
    public async Task DeleteAsync(Guid commentId, CancellationToken ct)
    {
        var comment = await _db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, ct)
            ?? throw ServiceException.NotFound("Comment not found.");

        var teamId = await ResolveTicketTeamAsync(comment.TicketId, ct);
        _currentUser.RequireTeamAccess(teamId);

        // Author or admin (moderation override, ADR-0015).
        var currentUserId = _currentUser.RequireUserId();
        if (comment.AuthorId != currentUserId && !_currentUser.IsAdmin)
            throw ServiceException.Forbidden("You can only delete your own comments.");

        var ticketId = comment.TicketId;
        _db.Comments.Remove(comment);
        await _db.SaveChangesAsync(ct);

        // Publish AFTER commit (§6.2): comment_deleted → activity ONLY (no notification/email, ADR-0015).
        // The actor is whoever performed the delete (author OR admin), not necessarily the comment author.
        var actorRow = await _db.Users.AsNoTracking()
            .Where(u => u.Id == currentUserId)
            .Select(u => new { u.Name, u.Email })
            .FirstOrDefaultAsync(ct);
        var actor = DisplayName(actorRow?.Name, actorRow?.Email ?? string.Empty);
        var summary = EventSummaries.CommentDeleted(actor);
        await _publisher.PublishAsync(new[]
        {
            new TicketEvent(EventType.CommentDeleted, ticketId, currentUserId, commentId, null, summary, summary)
        }, ct);
    }

    /// <summary>Server-side display name rule (§4.2): name?.trim() || email.</summary>
    private static string DisplayName(string? name, string email)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? email : trimmed;
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
