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
/// Wave 1 (ADR-0009): priority (F-03), due date + backend-computed isOverdue (F-08), and multiple
/// assignees (F-02) via full-set replace with team-member eligibility.
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
        Guid? teamId, string? type, Guid? epicId, string? search,
        string? priority, Guid? assigneeId, bool assignedToMe, string? dueFilter,
        CancellationToken ct)
    {
        if (teamId is null || teamId == Guid.Empty)
            throw ServiceException.Validation("teamId", "teamId is required.");

        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, ct);
        if (!teamExists)
            throw ServiceException.NotFound("Team not found.");

        // Board read is team-scoped: a member may only read a team they belong to (ADR-0007, §4.9).
        _currentUser.RequireTeamAccess(teamId.Value);

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

        // Priority filter (F-03): one of the canonical values or 400 (§4.1).
        if (!string.IsNullOrEmpty(priority))
        {
            if (!EnumCanonical.TryParsePriority(priority, out var parsedPriority))
                throw ServiceException.Validation("priority", "Invalid ticket priority.");
            query = query.Where(t => t.Priority == parsedPriority);
        }

        // Assignee filter (F-02): assignedToMe wins over assigneeId if both are sent (§4.2 precedence).
        var targetAssignee = assignedToMe ? _currentUser.RequireUserId()
            : (assigneeId is not null && assigneeId != Guid.Empty) ? assigneeId
            : null;
        if (targetAssignee is not null)
        {
            var target = targetAssignee.Value;
            query = query.Where(t => t.Assignees.Any(a => a.UserId == target));
        }

        // Due filter (F-08): single enum param (§4.3). today() from IClock (server single source).
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        if (!string.IsNullOrEmpty(dueFilter))
        {
            switch (dueFilter)
            {
                case "overdue":
                    query = query.Where(t => t.DueDate != null && t.DueDate < today && t.State != TicketState.Done);
                    break;
                case "has_due_date":
                    query = query.Where(t => t.DueDate != null);
                    break;
                case "no_due_date":
                    query = query.Where(t => t.DueDate == null);
                    break;
                default:
                    throw ServiceException.Validation("dueFilter", "Invalid due filter.");
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive substring over TITLE only (A24). EF.Functions.Like keeps the
            // filter at the DB level; '%' wildcards in the term are escaped.
            var pattern = "%" + EscapeLike(search.Trim()) + "%";
            query = query.Where(t => EF.Functions.Like(t.Title, pattern, "\\"));
        }

        // Project cards; order by modified desc within each column (A22). Assignees projected as
        // (id, name, email) so the display name is computed after materialization.
        var cards = await query
            .OrderByDescending(t => t.ModifiedAt)
            .Select(t => new
            {
                t.Id,
                t.Type,
                t.State,
                t.Priority,
                t.Title,
                t.EpicId,
                EpicTitle = t.Epic != null ? t.Epic.Title : null,
                t.DueDate,
                t.ModifiedAt,
                Assignees = t.Assignees
                    .OrderBy(a => a.CreatedAt)
                    .Select(a => new { a.UserId, a.User!.Name, a.User.Email })
                    .ToList()
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
                    EnumCanonical.ToCanonical(c.Priority),
                    c.Title,
                    c.EpicId,
                    c.EpicTitle,
                    c.DueDate,
                    ComputeIsOverdue(c.DueDate, c.State, today),
                    c.Assignees.Select(a => new AssigneeRefDto(a.UserId, DisplayName(a.Name, a.Email))).ToList(),
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
                CreatedByEmail = t.CreatedByUser != null ? t.CreatedByUser.Email : string.Empty,
                CreatedByName = t.CreatedByUser != null ? t.CreatedByUser.Name : null,
                Assignees = t.Assignees
                    .OrderBy(a => a.CreatedAt)
                    .Select(a => new { a.UserId, a.User!.Name, a.User.Email })
                    .ToList()
            })
            .FirstOrDefaultAsync(ct)
            ?? throw ServiceException.NotFound("Ticket not found.");

        // Resolve-then-check on the RESOURCE's team (not the request) — IDOR guard, §3.3 ordering.
        _currentUser.RequireTeamAccess(dto.Ticket.TeamId);

        var assignees = dto.Assignees
            .Select(a => new AssigneeRefDto(a.UserId, DisplayName(a.Name, a.Email)))
            .ToList();
        return ToDetail(dto.Ticket, dto.EpicTitle, dto.CreatedByEmail, dto.CreatedByName, assignees);
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

        // priority optional; default medium (§4.1); if provided must be valid.
        var priority = TicketPriority.Medium;
        if (!string.IsNullOrEmpty(request.Priority) && !EnumCanonical.TryParsePriority(request.Priority, out priority))
            errors["priority"] = new[] { "Invalid ticket priority. Allowed values: low, medium, high, urgent." };

        if (request.TeamId is null || request.TeamId == Guid.Empty)
            errors["teamId"] = new[] { "teamId is required." };

        if (errors.Count > 0)
            throw ServiceException.Validation("One or more fields are invalid.", errors);

        var teamId = request.TeamId!.Value;
        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, ct);
        if (!teamExists)
            throw ServiceException.Validation("teamId", "The specified team does not exist.");

        // Team-scope: the body teamId must be accessible (ADR-0007, §4.9).
        _currentUser.RequireTeamAccess(teamId);

        await ValidateEpicForTeamAsync(request.EpicId, teamId, ct);

        // Eligibility of any supplied assignees is a body-reference check (400 keyed userIds, §4.2).
        var eligibleAssignees = await ResolveEligibleAssigneesAsync(request.AssigneeIds, teamId, ct);

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
            Priority = priority,
            DueDate = request.DueDate,
            Title = title,
            Body = body,
            CreatedAt = now,
            ModifiedAt = now,
            CreatedBy = _currentUser.RequireUserId()
        };
        _db.Tickets.Add(ticket);

        // Apply the initial assignee set (if provided) in the same save (§4.2 create semantics).
        if (eligibleAssignees is not null)
            foreach (var userId in eligibleAssignees)
                _db.TicketAssignees.Add(new TicketAssignee
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    UserId = userId,
                    CreatedAt = now
                });

        await _db.SaveChangesAsync(ct);

        return await GetByIdAsync(ticket.Id, ct);
    }

    // ----- Update (API_CONTRACT §6.4) -----

    public async Task<TicketDetailDto> UpdateAsync(Guid id, UpdateTicketRequest request, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ServiceException.NotFound("Ticket not found.");
        // Resolve-then-check on the ticket's CURRENT team (IDOR guard, §3.3).
        _currentUser.RequireTeamAccess(ticket.TeamId);

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

        // priority is REQUIRED in the edit body (like type/state, §4.1).
        if (!EnumCanonical.TryParsePriority(request.Priority, out var priority))
            errors["priority"] = new[] { "Invalid ticket priority. Allowed values: low, medium, high, urgent." };

        if (request.TeamId is null || request.TeamId == Guid.Empty)
            errors["teamId"] = new[] { "teamId is required." };

        if (errors.Count > 0)
            throw ServiceException.Validation("One or more fields are invalid.", errors);

        var teamId = request.TeamId!.Value;
        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, ct);
        if (!teamExists)
            throw ServiceException.Validation("teamId", "The specified team does not exist.");

        // A member cannot MOVE a ticket into a team they do not belong to (§4.9, move-into-foreign-team).
        _currentUser.RequireTeamAccess(teamId);

        var newEpicId = NormalizeEpicId(request.EpicId);
        // Same-team-epic enforced even on team change / direct API (V16, EC5).
        await ValidateEpicForTeamAsync(newEpicId, teamId, ct);

        // assigneeIds: null/omitted ⇒ leave the set untouched (R-10); present ⇒ full-set replace after
        // validating eligibility against the (possibly new) team. Validate here so a bad ref is a 400
        // before any scalar mutation persists.
        var eligibleAssignees = await ResolveEligibleAssigneesAsync(request.AssigneeIds, teamId, ct);

        // WIP cap on the destination (UX §4.3): only blocks when the ticket actually arrives in a new
        // (team, state); staying put or leaving a state is allowed. Compared against stored values
        // before any mutation below.
        await EnforceWipLimitAsync(teamId, state, ticket.TeamId, ticket.State, ct);

        // Uniform no-op detection (§6.2, A19): compare every normalized editable field. Assignment is
        // metadata and does NOT participate in the modified_at diff (§4.2) — handled separately below.
        var changed =
            ticket.TeamId != teamId ||
            ticket.EpicId != newEpicId ||
            ticket.Type != type ||
            ticket.State != state ||
            ticket.Priority != priority ||
            ticket.DueDate != request.DueDate ||
            !string.Equals(ticket.Title, title, StringComparison.Ordinal) ||
            !string.Equals(ticket.Body, body, StringComparison.Ordinal);

        var assigneesChanged = false;
        if (eligibleAssignees is not null)
            assigneesChanged = await ApplyAssigneeSetAsync(ticket.Id, eligibleAssignees, ct);

        if (changed)
        {
            ticket.TeamId = teamId;
            ticket.EpicId = newEpicId;
            ticket.Type = type;
            ticket.State = state;
            ticket.Priority = priority;
            ticket.DueDate = request.DueDate;
            ticket.Title = title;
            ticket.Body = body;
            ticket.ModifiedAt = _clock.UtcNow; // assignment change alone never bumps modified_at (§4.2)
        }

        if (changed || assigneesChanged)
            await _db.SaveChangesAsync(ct);

        return await GetByIdAsync(ticket.Id, ct);
    }

    // ----- Set assignees (API_CONTRACT §4.2) -----

    /// <summary>
    /// Full-set replace of a ticket's assignees (F-02). Resolve-then-check the ticket's team (404 then
    /// 403), then validate each requested user id as a body reference (400 keyed <c>userIds</c> if
    /// unknown or ineligible). De-duplicates, diffs against the current set (add new, remove absent,
    /// leave unchanged) and does NOT advance <c>modified_at</c> (assignment is metadata, §4.2 / V21).
    /// Returns the updated ticket detail.
    /// </summary>
    public async Task<TicketDetailDto> SetAssigneesAsync(Guid id, SetAssigneesRequest request, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ServiceException.NotFound("Ticket not found.");
        // Resolve-then-check on the ticket's team before touching assignments (IDOR guard, §3.3).
        _currentUser.RequireTeamAccess(ticket.TeamId);

        // A null/omitted userIds on the dedicated endpoint means "clear to the empty set" (authoritative
        // full set); an eligible non-null set is validated as body references (400 keyed userIds).
        var desired = await ResolveEligibleAssigneesAsync(request.UserIds ?? Array.Empty<Guid>(), ticket.TeamId, ct)
            ?? new HashSet<Guid>();

        var changed = await ApplyAssigneeSetAsync(ticket.Id, desired, ct);
        if (changed)
            await _db.SaveChangesAsync(ct); // no modified_at bump (§4.2)

        return await GetByIdAsync(ticket.Id, ct);
    }

    // ----- Patch state (API_CONTRACT §6.5) -----

    public async Task<TicketStateDto> PatchStateAsync(Guid id, PatchTicketStateRequest request, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ServiceException.NotFound("Ticket not found.");
        // Resolve-then-check on the ticket's team before any state change (IDOR guard, §3.3).
        _currentUser.RequireTeamAccess(ticket.TeamId);

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
        // Resolve-then-check on the ticket's team before deleting (IDOR guard, §3.3).
        _currentUser.RequireTeamAccess(ticket.TeamId);

        // Comments and assignees cascade at the DB (V22 / ADR-0009). Explicitly remove tracked rows too
        // so the SQLite test provider (which honors FK cascade) and PG behave identically.
        var comments = await _db.Comments.Where(c => c.TicketId == id).ToListAsync(ct);
        if (comments.Count > 0)
            _db.Comments.RemoveRange(comments);

        var assignees = await _db.TicketAssignees.Where(a => a.TicketId == id).ToListAsync(ct);
        if (assignees.Count > 0)
            _db.TicketAssignees.RemoveRange(assignees);

        _db.Tickets.Remove(ticket);
        await _db.SaveChangesAsync(ct);
    }

    // ----- helpers -----

    private static Guid? NormalizeEpicId(Guid? epicId)
        => epicId is null || epicId == Guid.Empty ? null : epicId;

    /// <summary>Server-side display name rule everywhere a person is shown (§4.2): name?.trim() || email.</summary>
    private static string DisplayName(string? name, string email)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? email : trimmed;
    }

    /// <summary>isOverdue = dueDate != null &amp;&amp; dueDate &lt; today(UTC) &amp;&amp; state != done (§3.3).</summary>
    private static bool ComputeIsOverdue(DateOnly? dueDate, TicketState state, DateOnly today)
        => dueDate is { } d && d < today && state != TicketState.Done;

    /// <summary>
    /// Resolves the requested assignee ids to a validated, de-duplicated eligible set, or null when the
    /// request field itself is null (meaning "leave untouched", §4.2). Eligibility (ASSUMPTION
    /// W1-ASSIGN-ELIGIBILITY) = members of <paramref name="teamId"/> ∪ admins. An unknown OR ineligible
    /// id ⇒ 400 validation_error keyed <c>userIds</c> (a bad body reference → 400, ADR-0006 §B).
    /// </summary>
    private async Task<HashSet<Guid>?> ResolveEligibleAssigneesAsync(
        IReadOnlyList<Guid>? requested, Guid teamId, CancellationToken ct)
    {
        if (requested is null)
            return null; // "leave untouched" — caller decides whether that path is reachable

        var unique = new HashSet<Guid>();
        foreach (var userId in requested)
            if (userId != Guid.Empty)
                unique.Add(userId);

        if (unique.Count == 0)
            return unique; // authoritative empty set (clear all)

        // Every requested id must reference an existing user (unknown ⇒ 400).
        var existingCount = await _db.Users.CountAsync(u => unique.Contains(u.Id), ct);
        if (existingCount != unique.Count)
            throw ServiceException.Validation("userIds", "One or more users do not exist.");

        // Eligible = team members ∪ admins (one round-trip).
        var eligible = await _db.Users
            .Where(u => unique.Contains(u.Id)
                        && (u.IsAdmin || u.Memberships.Any(m => m.TeamId == teamId)))
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (eligible.Count != unique.Count)
            throw ServiceException.Validation("userIds", "One or more users are not members of this ticket's team.");

        return unique;
    }

    /// <summary>
    /// Diffs <paramref name="desired"/> against the ticket's current assignees: inserts the additions,
    /// removes the ones no longer present, leaves the rest. Returns true if anything changed. Does NOT
    /// save (the caller controls the transaction) and never bumps modified_at (§4.2). This is the single
    /// choke point Wave 2 hooks for added/removed notification fan-out (§6.5).
    /// </summary>
    private async Task<bool> ApplyAssigneeSetAsync(Guid ticketId, HashSet<Guid> desired, CancellationToken ct)
    {
        var existing = await _db.TicketAssignees.Where(a => a.TicketId == ticketId).ToListAsync(ct);
        var existingUserIds = existing.Select(a => a.UserId).ToHashSet();

        var changed = false;
        var now = _clock.UtcNow;

        foreach (var row in existing)
            if (!desired.Contains(row.UserId))
            {
                _db.TicketAssignees.Remove(row);
                changed = true;
            }

        foreach (var userId in desired)
            if (!existingUserIds.Contains(userId))
            {
                _db.TicketAssignees.Add(new TicketAssignee
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticketId,
                    UserId = userId,
                    CreatedAt = now
                });
                changed = true;
            }

        return changed;
    }

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

    private TicketDetailDto ToDetail(
        Ticket t, string? epicTitle, string createdByEmail, string? createdByName,
        IReadOnlyList<AssigneeRefDto> assignees)
        => new(
            t.Id,
            t.TeamId,
            t.EpicId,
            epicTitle,
            EnumCanonical.ToCanonical(t.Type),
            EnumCanonical.ToCanonical(t.State),
            EnumCanonical.ToCanonical(t.Priority),
            t.Title,
            t.Body,
            t.DueDate,
            ComputeIsOverdue(t.DueDate, t.State, DateOnly.FromDateTime(_clock.UtcNow)),
            assignees,
            t.CreatedAt,
            t.ModifiedAt,
            t.CreatedBy,
            createdByEmail,
            createdByName);
}
