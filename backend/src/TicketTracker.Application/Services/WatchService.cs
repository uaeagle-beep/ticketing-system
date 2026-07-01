using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Domain.Entities;

namespace TicketTracker.Application.Services;

/// <summary>
/// Watch/unwatch a ticket + list its watchers (Wave 2, §5.4, ADR-0013). Kept as a small service so
/// <see cref="TicketService"/> stays focused (WAVE2 §5.4 recommendation). Manual watch is a user
/// override on top of the auto-watch rules (§6.3); auto-watch itself is performed inside the mutating
/// service's transaction via the static <see cref="AddWatch"/> helper (idempotent, AnyAsync-guarded).
/// All endpoints are M(team of ticket): resolve ticket → 404, RequireTeamAccess → 403.
/// </summary>
public sealed class WatchService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public WatchService(IAppDbContext db, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>Watch the ticket (idempotent). Returns the caller's watching flag (always true on success).</summary>
    public async Task<WatchStatusDto> WatchAsync(Guid ticketId, CancellationToken ct)
    {
        var teamId = await ResolveTicketTeamAsync(ticketId, ct);
        _currentUser.RequireTeamAccess(teamId);

        var userId = _currentUser.RequireUserId();
        var added = await AddWatch(_db, ticketId, userId, _clock.UtcNow, ct);
        if (added)
            await _db.SaveChangesAsync(ct);

        return new WatchStatusDto(true);
    }

    /// <summary>Unwatch the ticket (idempotent). Returns the caller's watching flag (always false on success).</summary>
    public async Task<WatchStatusDto> UnwatchAsync(Guid ticketId, CancellationToken ct)
    {
        var teamId = await ResolveTicketTeamAsync(ticketId, ct);
        _currentUser.RequireTeamAccess(teamId);

        var userId = _currentUser.RequireUserId();
        var row = await _db.TicketWatchers.FirstOrDefaultAsync(w => w.TicketId == ticketId && w.UserId == userId, ct);
        if (row is not null)
        {
            _db.TicketWatchers.Remove(row);
            await _db.SaveChangesAsync(ct);
        }

        return new WatchStatusDto(false);
    }

    /// <summary>
    /// The caller's watching flag + the ticket's watcher list. Stale watchers (lost team access) are
    /// skipped at read (§6.3) so the list never leaks a person who can no longer see the ticket.
    /// </summary>
    public async Task<WatchersDto> ListWatchersAsync(Guid ticketId, CancellationToken ct)
    {
        var teamId = await ResolveTicketTeamAsync(ticketId, ct);
        _currentUser.RequireTeamAccess(teamId);

        var userId = _currentUser.RequireUserId();

        var rows = await _db.TicketWatchers.AsNoTracking()
            .Where(w => w.TicketId == ticketId)
            .Join(_db.Users, w => w.UserId, u => u.Id, (w, u) => u)
            // Skip stale watchers at read too (read-side of the team-scope rule, §6.3).
            .Where(u => !u.IsBlocked && (u.IsAdmin || u.Memberships.Any(m => m.TeamId == teamId)))
            .Select(u => new { u.Id, u.Name, u.Email })
            .ToListAsync(ct);

        var watchers = rows
            .Select(u => new WatcherRefDto(u.Id, DisplayName(u.Name, u.Email)))
            .OrderBy(w => w.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var watching = rows.Any(u => u.Id == userId);
        return new WatchersDto(watching, watchers);
    }

    /// <summary>
    /// Idempotent auto-watch primitive (§6.3): inserts a <see cref="TicketWatcher"/> if the user is not
    /// already watching. Does NOT save — the caller controls the transaction (auto-watch belongs with the
    /// mutation). Returns true if a row was added. Shared by <see cref="TicketService"/>/<see cref="CommentService"/>.
    /// </summary>
    public static async Task<bool> AddWatch(IAppDbContext db, Guid ticketId, Guid userId, DateTime now, CancellationToken ct)
    {
        var already = await db.TicketWatchers.AnyAsync(w => w.TicketId == ticketId && w.UserId == userId, ct);
        if (already)
            return false;

        db.TicketWatchers.Add(new TicketWatcher
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            UserId = userId,
            CreatedAt = now
        });
        return true;
    }

    private static string DisplayName(string? name, string email)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? email : trimmed;
    }

    private async Task<Guid> ResolveTicketTeamAsync(Guid ticketId, CancellationToken ct)
    {
        var teamId = await _db.Tickets.AsNoTracking()
            .Where(t => t.Id == ticketId)
            .Select(t => (Guid?)t.TeamId)
            .FirstOrDefaultAsync(ct);
        return teamId ?? throw ServiceException.NotFound("Ticket not found.");
    }
}
