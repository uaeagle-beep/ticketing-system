using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Validation;
using TicketTracker.Domain.Entities;

namespace TicketTracker.Application.Services;

/// <summary>
/// Team-scoped labels/tags CRUD (Wave 2, ADR-0016, WAVE2 §5.6/§8.2). Member-managed: every method
/// resolves the target team (404 if absent) then calls <c>RequireTeamAccess</c> (403 for a non-member
/// non-admin) — §3.3 resolve-then-check ordering (anti-IDOR). Name is non-empty after trim, ≤
/// <see cref="FieldLimits.LabelNameMax"/>, unique WITHIN the team case-insensitively via the normalized
/// companion column (mirrors <see cref="TeamService"/> team-name uniqueness); a collision → 409
/// <c>duplicate_label_name</c>. Color is validated to <c>#RRGGBB</c> and lowercased. Delete is disposable:
/// the label and its <c>ticket_labels</c> rows CASCADE away (no in-use guard, unlike epics).
/// Labels raise no activity/notification events (W2-LABEL-NOEVENTS).
/// </summary>
public sealed class LabelService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    // Authoritative color check (SQLite cannot easily regex-CHECK, so the service is the source of truth,
    // consistent with WIP-limit bounds). Compiled once; case-insensitive since we lowercase the value.
    private static readonly Regex ColorPattern = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public LabelService(IAppDbContext db, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>
    /// List a team's labels (GET /api/labels?teamId=), ordered by normalized name. M(team): resolve the
    /// team first (404 if absent) then check access (403). A missing teamId is a 400 (§5.6).
    /// </summary>
    public async Task<IReadOnlyList<LabelDto>> ListAsync(Guid? teamId, CancellationToken ct)
    {
        if (teamId is null || teamId == Guid.Empty)
            throw ServiceException.Validation("teamId", "teamId is required.");

        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, ct);
        if (!teamExists)
            throw ServiceException.NotFound("Team not found.");
        _currentUser.RequireTeamAccess(teamId.Value);

        return await _db.Labels.AsNoTracking()
            .Where(l => l.TeamId == teamId)
            .OrderBy(l => l.NameNormalized)
            .Select(l => new LabelDto(l.Id, l.TeamId, l.Name, l.Color))
            .ToListAsync(ct);
    }

    /// <summary>Create a label in a team (POST /api/labels). 409 on a per-team duplicate normalized name.</summary>
    public async Task<LabelDto> CreateAsync(CreateLabelRequest request, CancellationToken ct)
    {
        if (request.TeamId is null || request.TeamId == Guid.Empty)
            throw ServiceException.Validation("teamId", "teamId is required.");

        var teamId = request.TeamId.Value;
        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, ct);
        if (!teamExists)
            throw ServiceException.NotFound("Team not found.");
        _currentUser.RequireTeamAccess(teamId);

        var (name, normalized, color) = ValidateNameAndColor(request.Name, request.Color);

        var collision = await _db.Labels.AnyAsync(l => l.TeamId == teamId && l.NameNormalized == normalized, ct);
        if (collision)
            throw new ServiceException(ServiceErrorCode.DuplicateLabelName,
                "A label with this name already exists in this team.");

        var label = new Label
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            Name = name,
            NameNormalized = normalized,
            Color = color,
            CreatedAt = _clock.UtcNow
        };
        _db.Labels.Add(label);
        await _db.SaveChangesAsync(ct);

        return new LabelDto(label.Id, label.TeamId, label.Name, label.Color);
    }

    /// <summary>Rename / recolor a label (PUT /api/labels/{id}). Team is immutable; no-op rule for name+color.</summary>
    public async Task<LabelDto> UpdateAsync(Guid id, UpdateLabelRequest request, CancellationToken ct)
    {
        var label = await _db.Labels.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw ServiceException.NotFound("Label not found.");
        _currentUser.RequireTeamAccess(label.TeamId);

        var (name, normalized, color) = ValidateNameAndColor(request.Name, request.Color);

        // No-op rule (mirrors TeamService rename, §5.6): normalized new name + color equal stored ⇒
        // persist nothing, return the unchanged object.
        if (normalized == label.NameNormalized && color == label.Color)
            return new LabelDto(label.Id, label.TeamId, label.Name, label.Color);

        // Uniqueness against a DIFFERENT label in the same team.
        var collision = await _db.Labels.AnyAsync(
            l => l.TeamId == label.TeamId && l.NameNormalized == normalized && l.Id != id, ct);
        if (collision)
            throw new ServiceException(ServiceErrorCode.DuplicateLabelName,
                "A label with this name already exists in this team.");

        label.Name = name;
        label.NameNormalized = normalized;
        label.Color = color;
        await _db.SaveChangesAsync(ct);

        return new LabelDto(label.Id, label.TeamId, label.Name, label.Color);
    }

    /// <summary>
    /// Delete a label (DELETE /api/labels/{id}). Disposable: the label and its <c>ticket_labels</c> rows
    /// CASCADE away; no in-use 409 guard (a label is throwaway metadata, ADR-0016). 204 on success.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var label = await _db.Labels.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw ServiceException.NotFound("Label not found.");
        _currentUser.RequireTeamAccess(label.TeamId);

        // ticket_labels CASCADE at the DB (both providers); explicitly remove tracked rows too so the SQLite
        // test provider and PG behave identically (mirrors TicketService.DeleteAsync cascades).
        var tags = await _db.TicketLabels.Where(tl => tl.LabelId == id).ToListAsync(ct);
        if (tags.Count > 0)
            _db.TicketLabels.RemoveRange(tags);

        _db.Labels.Remove(label);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Validate + normalize name and color, throwing 400 with per-field errors on failure. Name: required,
    /// trimmed, ≤ LabelNameMax. Color: required, matches <c>#RRGGBB</c> (case-insensitive), lowercased.
    /// </summary>
    private static (string Name, string Normalized, string Color) ValidateNameAndColor(string? rawName, string? rawColor)
    {
        var errors = new Dictionary<string, string[]>();

        var name = Normalization.Trim(rawName);
        if (Normalization.IsBlank(name))
            errors["name"] = new[] { "Label name is required." };
        else if (name.Length > FieldLimits.LabelNameMax)
            errors["name"] = new[] { $"Label name must be at most {FieldLimits.LabelNameMax} characters." };

        var color = Normalization.Trim(rawColor);
        if (Normalization.IsBlank(color))
            errors["color"] = new[] { "Color is required." };
        else if (!ColorPattern.IsMatch(color))
            errors["color"] = new[] { "Color must be a hex value like #3b82f6." };

        if (errors.Count > 0)
            throw ServiceException.Validation("One or more fields are invalid.", errors);

        return (name, Normalization.NormalizeKey(name), color.ToLowerInvariant());
    }
}
