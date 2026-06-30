using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Validation;
using TicketTracker.Domain.Entities;

namespace TicketTracker.Application.Services;

/// <summary>
/// Teams CRUD (E2). Enforces non-empty trimmed name + case-insensitive uniqueness (V8),
/// no-op rename semantics (A10), and the delete-guard 409 when the team has any ticket or
/// epic (V9). All deletes are backed by FK RESTRICT as a backstop (ARCHITECTURE §4.3).
/// </summary>
public sealed class TeamService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public TeamService(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TeamDto>> ListAsync(CancellationToken ct)
    {
        // Project counts at the DB level so the list scales (Wireframe 4 columns).
        var teams = await _db.Teams.AsNoTracking()
            .OrderBy(t => t.NameNormalized)
            .Select(t => new TeamDto(
                t.Id,
                t.Name,
                t.Tickets.Count(),
                t.Epics.Count(),
                t.CreatedAt,
                t.ModifiedAt))
            .ToListAsync(ct);
        return teams;
    }

    public async Task<TeamDto> CreateAsync(CreateTeamRequest request, CancellationToken ct)
    {
        var name = Normalization.Trim(request.Name);
        if (Normalization.IsBlank(name))
            throw ServiceException.Validation("name", "Team name is required.");
        if (name.Length > FieldLimits.TeamNameMax)
            throw ServiceException.Validation("name", $"Team name must be at most {FieldLimits.TeamNameMax} characters.");

        var normalized = Normalization.NormalizeKey(name);
        var exists = await _db.Teams.AnyAsync(t => t.NameNormalized == normalized, ct);
        if (exists)
            throw new ServiceException(ServiceErrorCode.DuplicateTeamName,
                "A team with this name already exists.");

        var now = _clock.UtcNow;
        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = name,
            NameNormalized = normalized,
            CreatedAt = now,
            ModifiedAt = now
        };
        _db.Teams.Add(team);
        await _db.SaveChangesAsync(ct);

        return new TeamDto(team.Id, team.Name, 0, 0, team.CreatedAt, team.ModifiedAt);
    }

    public async Task<TeamDto> RenameAsync(Guid id, UpdateTeamRequest request, CancellationToken ct)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ServiceException.NotFound("Team not found.");

        var name = Normalization.Trim(request.Name);
        if (Normalization.IsBlank(name))
            throw ServiceException.Validation("name", "Team name is required.");
        if (name.Length > FieldLimits.TeamNameMax)
            throw ServiceException.Validation("name", $"Team name must be at most {FieldLimits.TeamNameMax} characters.");

        var normalized = Normalization.NormalizeKey(name);

        // No-op rule (A10): normalized new name equals stored => persist nothing, no modified bump.
        if (normalized == team.NameNormalized)
            return await ToDtoAsync(team, ct);

        // Uniqueness against a DIFFERENT team (US-TEAM-2).
        var collision = await _db.Teams.AnyAsync(t => t.NameNormalized == normalized && t.Id != id, ct);
        if (collision)
            throw new ServiceException(ServiceErrorCode.DuplicateTeamName,
                "A team with this name already exists.");

        team.Name = name;
        team.NameNormalized = normalized;
        team.ModifiedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await ToDtoAsync(team, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ServiceException.NotFound("Team not found.");

        var hasTickets = await _db.Tickets.AnyAsync(t => t.TeamId == id, ct);
        var hasEpics = await _db.Epics.AnyAsync(e => e.TeamId == id, ct);
        if (hasTickets || hasEpics)
            throw new ServiceException(ServiceErrorCode.TeamHasChildren,
                "Cannot delete a team that still has tickets or epics. Remove them first.");

        _db.Teams.Remove(team);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<TeamDto> ToDtoAsync(Team team, CancellationToken ct)
    {
        var ticketCount = await _db.Tickets.CountAsync(t => t.TeamId == team.Id, ct);
        var epicCount = await _db.Epics.CountAsync(e => e.TeamId == team.Id, ct);
        return new TeamDto(team.Id, team.Name, ticketCount, epicCount, team.CreatedAt, team.ModifiedAt);
    }
}
