using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Validation;
using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Services;

/// <summary>
/// Tickets (E4) + board read (E6). Enforces strict enum parsing (V13/V14), existing team
/// (V15), same-team-epic on EVERY create/update (V16, ARCHITECTURE §6.3), non-empty
/// title/body (V17), server-set created_at/modified_at/created_by (V18), and the uniform
/// modified_at no-op semantics (V19/V20, §6.2). Deleting a ticket cascades to its comments (V22).
/// </summary>
public sealed class TicketService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public TicketService(IAppDbContext db, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    // ----- Board read (API_CONTRACT §6.1) -----

    public async Task<BoardDto> GetBoardAsync(
        Guid? teamId, string? type, Guid? epicId, string? search, CancellationToken ct)
    {
        if (teamId is null || teamId == Guid.Empty)
            throw ServiceException.Validation("teamId", "teamId is required.");

        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, ct);
        if (!teamExists)
            throw ServiceException.NotFound("Team not found.");

        var query = _db.Tickets.AsNoTracking().Where(t => t.TeamId == teamId);

        // Optional filters combine with AND (A24).
        if (!string.IsNullOrEmpty(type))
        {
            if (!EnumCanonical.TryParseType(type, out var parsedType))
                throw ServiceException.Validation("type", "Invalid ticket type.");
            query = query.Where(t => t.Type == parsedType);
        }

        if (epicId is not null && epicId != Guid.Empty)
        {
            query = query.Where(t => t.EpicId == epicId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive substring over TITLE only (A24). EF.Functions.Like keeps the
            // filter at the DB level; '%' wildcards in the term are escaped.
            var pattern = "%" + EscapeLike(search.Trim()) + "%";
            query = query.Where(t => EF.Functions.Like(t.Title, pattern, "\\"));
        }

        // Project cards; order by modified desc within each column (A22).
        var cards = await query
            .OrderByDescending(t => t.ModifiedAt)
            .Select(t => new
            {
                t.Id,
                t.Type,
                t.State,
                t.Title,
                t.EpicId,
                EpicTitle = t.Epic != null ? t.Epic.Title : null,
                t.ModifiedAt
            })
            .ToListAsync(ct);

        var grouped = cards
            .GroupBy(c => c.State)
            .ToDictionary(g => g.Key, g => g.ToList());

        // UNFILTERED per-state totals for the team — the WIP badge "N / max" numerator and the
        // full/over comparison must ignore the type/epic/search filters (UX §3.1, A23).
        var totalsByState = await _db.Tickets.AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .GroupBy(t => t.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.State, g => g.Count, ct);

        // Team's per-state caps (absent state = unlimited).
        var limitsByState = await _db.WipLimits.AsNoTracking()
            .Where(w => w.TeamId == teamId)
            .ToDictionaryAsync(w => w.State, w => w.MaxCount, ct);

        // Always emit exactly five columns in workflow order (FR-E6-2), even when empty.
        var columns = new List<BoardColumnDto>(EnumCanonical.WorkflowOrder.Length);
        foreach (var state in EnumCanonical.WorkflowOrder)
        {
            var inState = grouped.TryGetValue(state, out var list) ? list : new();
            var tickets = inState
                .Select(c => new TicketCardDto(
                    c.Id,
                    EnumCanonical.ToCanonical(c.Type),
                    EnumCanonical.ToCanonical(c.State),
                    c.Title,
                    c.EpicId,
                    c.EpicTitle,
                    c.ModifiedAt))
                .ToList();
            var total = totalsByState.TryGetValue(state, out var t) ? t : 0;
            int? wipLimit = limitsByState.TryGetValue(EnumCanonical.ToCanonical(state), out var max) ? max : null;
            columns.Add(new BoardColumnDto(EnumCanonical.ToCanonical(state), tickets.Count, total, wipLimit, tickets));
        }

        return new BoardDto(teamId.Value, cards.Count, columns);
    }

    // ----- Detail (API_CONTRACT §6.2) -----

    public async Task<TicketDetailDto> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var dto = await _db.Tickets.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new
            {
                Ticket = t,
                EpicTitle = t.Epic != null ? t.Epic.Title : null,
                CreatedByEmail = t.CreatedByUser != null ? t.CreatedByUser.Email : string.Empty
            })
            .FirstOrDefaultAsync(ct)
            ?? throw ServiceException.NotFound("Ticket not found.");

        return ToDetail(dto.Ticket, dto.EpicTitle, dto.CreatedByEmail);
    }

    // ----- Create (API_CONTRACT §6.3) -----

    public async Task<TicketDetailDto> CreateAsync(CreateTicketRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        var title = Normalization.Trim(request.Title);
        if (Normalization.IsBlank(title))
            errors["title"] = new[] { "Title is required." };
        else if (title.Length > FieldLimits.TicketTitleMax)
            errors["title"] = new[] { $"Title must be at most {FieldLimits.TicketTitleMax} characters." };

        var body = Normalization.Trim(request.Body);
        if (Normalization.IsBlank(body))
            errors["body"] = new[] { "Body is required." };
        else if (body.Length > FieldLimits.TicketBodyMax)
            errors["body"] = new[] { $"Body must be at most {FieldLimits.TicketBodyMax} characters." };

        if (!EnumCanonical.TryParseType(request.Type, out var type))
            errors["type"] = new[] { "Invalid ticket type. Allowed values: bug, feature, fix." };

        // state optional; default new (A15); if provided must be valid (V14).
        var state = TicketState.New;
        if (!string.IsNullOrEmpty(request.State) && !EnumCanonical.TryParseState(request.State, out state))
            errors["state"] = new[] { "Invalid ticket state." };

        if (request.TeamId is null || request.TeamId == Guid.Empty)
            errors["teamId"] = new[] { "teamId is required." };

        if (errors.Count > 0)
            throw ServiceException.Validation("One or more fields are invalid.", errors);

        var teamId = request.TeamId!.Value;
        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, ct);
        if (!teamExists)
            throw ServiceException.Validation("teamId", "The specified team does not exist.");

        await ValidateEpicForTeamAsync(request.EpicId, teamId, ct);

        // WIP cap on the destination state (UX §4.3). A new ticket is always an arrival.
        await EnforceWipLimitAsync(teamId, state, currentTeamId: null, currentState: null, ct);

        var now = _clock.UtcNow;
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            EpicId = NormalizeEpicId(request.EpicId),
            Type = type,
            State = state,
            Title = title,
            Body = body,
            CreatedAt = now,
            ModifiedAt = now,
            CreatedBy = _currentUser.RequireUserId()
        };
        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);

        return await GetByIdAsync(ticket.Id, ct);
    }

    // ----- Update (API_CONTRACT §6.4) -----

    public async Task<TicketDetailDto> UpdateAsync(Guid id, UpdateTicketRequest request, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ServiceException.NotFound("Ticket not found.");

        var errors = new Dictionary<string, string[]>();

        var title = Normalization.Trim(request.Title);
        if (Normalization.IsBlank(title))
            errors["title"] = new[] { "Title is required." };
        else if (title.Length > FieldLimits.TicketTitleMax)
            errors["title"] = new[] { $"Title must be at most {FieldLimits.TicketTitleMax} characters." };

        var body = Normalization.Trim(request.Body);
        if (Normalization.IsBlank(body))
            errors["body"] = new[] { "Body is required." };
        else if (body.Length > FieldLimits.TicketBodyMax)
            errors["body"] = new[] { $"Body must be at most {FieldLimits.TicketBodyMax} characters." };

        if (!EnumCanonical.TryParseType(request.Type, out var type))
            errors["type"] = new[] { "Invalid ticket type. Allowed values: bug, feature, fix." };

        if (!EnumCanonical.TryParseState(request.State, out var state))
            errors["state"] = new[] { "Invalid ticket state." };

        if (request.TeamId is null || request.TeamId == Guid.Empty)
            errors["teamId"] = new[] { "teamId is required." };

        if (errors.Count > 0)
            throw ServiceException.Validation("One or more fields are invalid.", errors);

        var teamId = request.TeamId!.Value;
        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, ct);
        if (!teamExists)
            throw ServiceException.Validation("teamId", "The specified team does not exist.");

        var newEpicId = NormalizeEpicId(request.EpicId);
        // Same-team-epic enforced even on team change / direct API (V16, EC5).
        await ValidateEpicForTeamAsync(newEpicId, teamId, ct);

        // WIP cap on the destination (UX §4.3): only blocks when the ticket actually arrives in a new
        // (team, state); staying put or leaving a state is allowed. Compared against stored values
        // before any mutation below.
        await EnforceWipLimitAsync(teamId, state, ticket.TeamId, ticket.State, ct);

        // Uniform no-op detection (§6.2, A19): compare every normalized editable field.
        var changed =
            ticket.TeamId != teamId ||
            ticket.EpicId != newEpicId ||
            ticket.Type != type ||
            ticket.State != state ||
            !string.Equals(ticket.Title, title, StringComparison.Ordinal) ||
            !string.Equals(ticket.Body, body, StringComparison.Ordinal);

        if (changed)
        {
            ticket.TeamId = teamId;
            ticket.EpicId = newEpicId;
            ticket.Type = type;
            ticket.State = state;
            ticket.Title = title;
            ticket.Body = body;
            ticket.ModifiedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return await GetByIdAsync(ticket.Id, ct);
    }

    // ----- Patch state (API_CONTRACT §6.5) -----

    public async Task<TicketStateDto> PatchStateAsync(Guid id, PatchTicketStateRequest request, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ServiceException.NotFound("Ticket not found.");

        if (!EnumCanonical.TryParseState(request.State, out var state))
            throw ServiceException.Validation("state", "Invalid ticket state.");

        // No-op when unchanged: do not advance modified_at (§6.5 note). A same-state drop is therefore
        // never blocked by a WIP cap; only a real move into a different (capped) state is checked (UX §4.2).
        if (ticket.State != state)
        {
            await EnforceWipLimitAsync(ticket.TeamId, state, ticket.TeamId, ticket.State, ct);
            ticket.State = state;
            ticket.ModifiedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return new TicketStateDto(ticket.Id, EnumCanonical.ToCanonical(ticket.State), ticket.ModifiedAt);
    }

    // ----- Delete (API_CONTRACT §6.6) -----

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ServiceException.NotFound("Ticket not found.");

        // Comments cascade at the DB (V22). Explicitly remove tracked comments too so the
        // SQLite test provider (which honors FK cascade) and PG behave identically.
        var comments = await _db.Comments.Where(c => c.TicketId == id).ToListAsync(ct);
        if (comments.Count > 0)
            _db.Comments.RemoveRange(comments);

        _db.Tickets.Remove(ticket);
        await _db.SaveChangesAsync(ct);
    }

    // ----- helpers -----

    private static Guid? NormalizeEpicId(Guid? epicId)
        => epicId is null || epicId == Guid.Empty ? null : epicId;

    /// <summary>
    /// Enforce the per-team, per-state WIP cap on a ticket ARRIVING in <paramref name="targetTeamId"/>/
    /// <paramref name="targetState"/> (UX §1.3, §4). Rules:
    ///  - A move WITHIN the same (team, state) is a no-op and is always allowed (pass
    ///    <paramref name="currentTeamId"/>/<paramref name="currentState"/> for an existing ticket; null for create).
    ///  - Leaving a state is never blocked (this only guards the destination).
    ///  - If a cap exists for (targetTeam, targetState) and the UNFILTERED count already in that state for that
    ///    team is &gt;= the cap, the arrival is rejected with 409 wip_limit_reached. The count is the live
    ///    destination state, so lowering a limit below the current count still blocks NEW arrivals while leaving
    ///    existing over-limit tickets in place.
    /// </summary>
    private async Task EnforceWipLimitAsync(
        Guid targetTeamId, TicketState targetState,
        Guid? currentTeamId, TicketState? currentState, CancellationToken ct)
    {
        // Already in the destination (same team + same state) => not a new arrival; always allowed.
        if (currentTeamId == targetTeamId && currentState == targetState)
            return;

        var stateKey = EnumCanonical.ToCanonical(targetState);
        var limit = await _db.WipLimits.AsNoTracking()
            .Where(w => w.TeamId == targetTeamId && w.State == stateKey)
            .Select(w => (int?)w.MaxCount)
            .FirstOrDefaultAsync(ct);
        if (limit is null)
            return; // unlimited

        var inTarget = await _db.Tickets
            .CountAsync(t => t.TeamId == targetTeamId && t.State == targetState, ct);
        if (inTarget >= limit.Value)
            throw new ServiceException(ServiceErrorCode.WipLimitReached, WipLimits.ReachedMessage);
    }

    /// <summary>
    /// If an epic is referenced it must exist AND belong to the target team (V16, §6.3).
    /// A non-existent epic in the body is a 400 validation_error; a cross-team epic is
    /// 400 epic_team_mismatch.
    /// </summary>
    private async Task ValidateEpicForTeamAsync(Guid? epicId, Guid teamId, CancellationToken ct)
    {
        var normalized = NormalizeEpicId(epicId);
        if (normalized is null) return;

        var epic = await _db.Epics.AsNoTracking().FirstOrDefaultAsync(e => e.Id == normalized, ct);
        if (epic is null)
            throw ServiceException.Validation("epicId", "The specified epic does not exist.");

        if (epic.TeamId != teamId)
            throw new ServiceException(ServiceErrorCode.EpicTeamMismatch,
                "The selected epic belongs to a different team than the ticket.");
    }

    private static string EscapeLike(string input)
        => input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static TicketDetailDto ToDetail(Ticket t, string? epicTitle, string createdByEmail)
        => new(
            t.Id,
            t.TeamId,
            t.EpicId,
            epicTitle,
            EnumCanonical.ToCanonical(t.Type),
            EnumCanonical.ToCanonical(t.State),
            t.Title,
            t.Body,
            t.CreatedAt,
            t.ModifiedAt,
            t.CreatedBy,
            createdByEmail);
}
