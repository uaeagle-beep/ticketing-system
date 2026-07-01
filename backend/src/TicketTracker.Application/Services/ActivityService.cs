using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;

namespace TicketTracker.Application.Services;

/// <summary>
/// Per-ticket activity timeline read (Wave 2, §5.5, ADR-0012). Team-scoped: resolve ticket → 404,
/// RequireTeamAccess → 403 (404-then-403, ADR-0007). Newest-first with keyset pagination (same cursor
/// scheme as notifications). This is the user-facing "what happened to this ticket", distinct from any
/// SEC-3 admin audit log (§7bis).
/// </summary>
public sealed class ActivityService
{
    public const int MinLimit = 1;
    public const int MaxLimit = 100;
    public const int DefaultLimit = 50;

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ActivityService(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ActivityListDto> ListAsync(Guid ticketId, int? limit, string? cursor, CancellationToken ct)
    {
        // Resolve the ticket's team (404 if absent) then check access (403) — §3.3.
        var teamId = await _db.Tickets.AsNoTracking()
            .Where(t => t.Id == ticketId)
            .Select(t => (Guid?)t.TeamId)
            .FirstOrDefaultAsync(ct)
            ?? throw ServiceException.NotFound("Ticket not found.");
        _currentUser.RequireTeamAccess(teamId);

        var take = Math.Clamp(limit ?? DefaultLimit, MinLimit, MaxLimit);

        var query = _db.ActivityEntries.AsNoTracking()
            .Where(a => a.TicketId == ticketId);

        var decoded = KeysetCursor.Decode(cursor);
        if (decoded is { } c)
        {
            query = query.Where(a =>
                a.CreatedAt < c.CreatedAt ||
                (a.CreatedAt == c.CreatedAt && a.Id.CompareTo(c.Id) < 0));
        }

        var rows = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(take + 1)
            .Select(a => new
            {
                a.Id,
                a.EventType,
                a.Summary,
                a.ActorId,
                ActorName = a.Actor != null ? a.Actor.Name : null,
                ActorEmail = a.Actor != null ? a.Actor.Email : string.Empty,
                a.CreatedAt
            })
            .ToListAsync(ct);

        var hasMore = rows.Count > take;
        var page = hasMore ? rows.Take(take).ToList() : rows;

        var items = page
            .Select(a => new ActivityEntryDto(
                a.Id, a.EventType, a.Summary, a.ActorId, DisplayName(a.ActorName, a.ActorEmail), a.CreatedAt))
            .ToList();

        string? nextCursor = hasMore && page.Count > 0
            ? KeysetCursor.Encode(page[^1].CreatedAt, page[^1].Id)
            : null;

        return new ActivityListDto(items, hasMore, nextCursor);
    }

    private static string DisplayName(string? name, string email)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? email : trimmed;
    }
}
