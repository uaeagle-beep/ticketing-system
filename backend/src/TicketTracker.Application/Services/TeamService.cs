using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Validation;
using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;

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
    private readonly ICurrentUser _currentUser;

    public TeamService(IAppDbContext db, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<TeamDto>> ListAsync(CancellationToken ct)
    {
        // Membership-scoped list (ADR-0007): admin sees all teams; a member sees only their teams.
        var query = _db.Teams.AsNoTracking().AsQueryable();
        if (!_currentUser.IsAdmin)
        {
            var teamIds = _currentUser.TeamIds;
            query = query.Where(t => teamIds.Contains(t.Id));
        }

        // Project counts + WIP limits at the DB level so the list scales (Wireframe 4 columns).
        var rows = await query
            .OrderBy(t => t.NameNormalized)
            .Select(t => new
            {
                t.Id,
                t.Name,
                TicketCount = t.Tickets.Count(),
                EpicCount = t.Epics.Count(),
                t.CreatedAt,
                t.ModifiedAt,
                WipLimits = t.WipLimits.Select(w => new { w.State, w.MaxCount }).ToList()
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new TeamDto(
                r.Id, r.Name, r.TicketCount, r.EpicCount, r.CreatedAt, r.ModifiedAt,
                Services.WipLimits.ToMap(r.WipLimits.Select(w => (w.State, w.MaxCount)))))
            .ToList();
    }

    /// <summary>
    /// List a team's members for pickers (Wave-1 debt, WAVE2 §5.8 / ADR-0017). M(team): resolve the
    /// team first (404 if absent) then check access (403 for a non-member non-admin) — §3.3 ordering.
    /// Returns the team's members only (admins are global and use the admin surface); each row carries
    /// <c>displayName = name?.Trim() || email</c> and the user's global <c>isAdmin</c> flag. Ordered by
    /// display name (case-insensitive).
    /// </summary>
    public async Task<IReadOnlyList<TeamMemberDto>> ListMembersAsync(Guid id, CancellationToken ct)
    {
        // Resolve then check (404-then-403): a non-member must not learn whether the team exists.
        var teamExists = await _db.Teams.AnyAsync(t => t.Id == id, ct);
        if (!teamExists)
            throw ServiceException.NotFound("Team not found.");
        _currentUser.RequireTeamAccess(id);

        var rows = await _db.UserTeams.AsNoTracking()
            .Where(m => m.TeamId == id)
            .Select(m => new { m.User!.Id, m.User.Email, m.User.Name, m.User.IsAdmin })
            .ToListAsync(ct);

        // displayName = name?.Trim() || email (computed in memory so Trim/coalesce need not translate).
        return rows
            .Select(u => new TeamMemberDto(
                u.Id,
                string.IsNullOrWhiteSpace(u.Name) ? u.Email : u.Name!.Trim(),
                u.IsAdmin))
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<TeamDto> CreateAsync(CreateTeamRequest request, CancellationToken ct)
    {
        _currentUser.RequireAdmin(); // team CRUD is admin-only (ADR-0007, §4.9)

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

        // A fresh team has no WIP limits (all states unlimited, V-WIP-1).
        return new TeamDto(team.Id, team.Name, 0, 0, team.CreatedAt, team.ModifiedAt,
            Services.WipLimits.ToMap(Array.Empty<WipLimit>()));
    }

    public async Task<TeamDto> RenameAsync(Guid id, UpdateTeamRequest request, CancellationToken ct)
    {
        _currentUser.RequireAdmin(); // team rename is admin-only (ADR-0007, §4.9)

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
        _currentUser.RequireAdmin(); // team delete is admin-only (ADR-0007, §4.9)

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

    /// <summary>
    /// Replace this team's per-state WIP limits (API_CONTRACT §4, UX §2.2). The request is a map of
    /// canonical state -> value where null/omitted = unlimited and an integer in [1, 999] is a cap.
    /// Validation (rejected with 400 validation_error + per-state errors):
    ///   unknown state key; 0; negative; fractional/non-numeric; out of [1, 999].
    /// Setting a cap below the current count is allowed — only NEW arrivals are blocked, existing
    /// over-limit tickets stay (UX §1.3, §5.1). 404 if the team does not exist.
    /// </summary>
    public async Task<TeamDto> SetWipLimitsAsync(Guid id, UpdateWipLimitsRequest request, CancellationToken ct)
    {
        // M(team): resolve first (404 if absent) then check access (403 if not a member) — §3.3 ordering.
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ServiceException.NotFound("Team not found.");
        _currentUser.RequireTeamAccess(team.Id);

        var input = request.WipLimits ?? new Dictionary<string, JsonElement>();

        // Parse + validate every provided entry, collecting per-state errors keyed by the state name.
        var errors = new Dictionary<string, string[]>();
        var desired = new Dictionary<string, int>(StringComparer.Ordinal); // state -> cap (only set states)

        foreach (var (rawState, rawValue) in input)
        {
            if (!EnumCanonical.TryParseState(rawState, out _))
            {
                errors[rawState] = new[] { "Unknown board state." };
                continue;
            }

            // null => unlimited (no row). Anything else must be a whole number in [1, 999].
            if (rawValue.ValueKind == JsonValueKind.Null)
                continue;

            if (rawValue.ValueKind != JsonValueKind.Number
                || !rawValue.TryGetInt32(out var value)) // rejects fractional and non-numeric (e.g. 2.5, "abc")
            {
                errors[rawState] = new[] { "Enter a whole number of 1 or more, or leave blank for no limit." };
                continue;
            }

            if (value > FieldLimits.WipLimitMax)
                errors[rawState] = new[] { $"Enter a number no greater than {FieldLimits.WipLimitMax}." };
            else if (value < FieldLimits.WipLimitMin)
                errors[rawState] = new[] { "Enter a whole number of 1 or more, or leave blank for no limit." };
            else
                desired[rawState] = value;
        }

        if (errors.Count > 0)
            throw ServiceException.Validation("One or more WIP limits are invalid.", errors);

        // Reconcile persisted rows with the desired set: a state present in `desired` is upserted; a state
        // explicitly set to null OR omitted stays/becomes unlimited (row removed if present). This makes the
        // request the full authoritative limit set for the team (UX §2.3 batch save of all five fields).
        var existing = await _db.WipLimits.Where(w => w.TeamId == id).ToListAsync(ct);
        var byState = existing.ToDictionary(w => w.State, StringComparer.Ordinal);
        var changed = false;

        foreach (var state in EnumCanonical.WorkflowOrder)
        {
            var key = EnumCanonical.ToCanonical(state);
            var hasDesired = desired.TryGetValue(key, out var max);
            byState.TryGetValue(key, out var row);

            if (hasDesired)
            {
                if (row is null)
                {
                    _db.WipLimits.Add(new WipLimit { Id = Guid.NewGuid(), TeamId = id, State = key, MaxCount = max });
                    changed = true;
                }
                else if (row.MaxCount != max)
                {
                    row.MaxCount = max;
                    changed = true;
                }
            }
            else if (row is not null)
            {
                _db.WipLimits.Remove(row);
                changed = true;
            }
        }

        if (changed)
            await _db.SaveChangesAsync(ct);

        return await ToDtoAsync(team, ct);
    }

    private async Task<TeamDto> ToDtoAsync(Team team, CancellationToken ct)
    {
        var ticketCount = await _db.Tickets.CountAsync(t => t.TeamId == team.Id, ct);
        var epicCount = await _db.Epics.CountAsync(e => e.TeamId == team.Id, ct);
        var limits = await _db.WipLimits.AsNoTracking()
            .Where(w => w.TeamId == team.Id)
            .Select(w => new { w.State, w.MaxCount })
            .ToListAsync(ct);
        return new TeamDto(team.Id, team.Name, ticketCount, epicCount, team.CreatedAt, team.ModifiedAt,
            Services.WipLimits.ToMap(limits.Select(w => (w.State, w.MaxCount))));
    }
}
