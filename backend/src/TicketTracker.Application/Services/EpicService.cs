using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Validation;
using TicketTracker.Domain.Entities;

namespace TicketTracker.Application.Services;

/// <summary>
/// Epics CRUD (E3). Team is required + must exist at create and is immutable thereafter
/// (FR-E3-1, A13). Title non-empty after trim (V11); duplicates allowed (A11). No-op edit
/// does not advance modified_at (A14). Delete is guarded by referencing-ticket count → 409
/// (V12), backed by FK RESTRICT.
/// </summary>
public sealed class EpicService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public EpicService(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<EpicDto>> ListByTeamAsync(Guid? teamId, CancellationToken ct)
    {
        // teamId is required (API_CONTRACT §5.1).
        if (teamId is null || teamId == Guid.Empty)
            throw ServiceException.Validation("teamId", "teamId is required.");

        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, ct);
        if (!teamExists)
            throw ServiceException.NotFound("Team not found.");

        return await _db.Epics.AsNoTracking()
            .Where(e => e.TeamId == teamId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new EpicDto(
                e.Id,
                e.TeamId,
                e.Title,
                e.Description,
                e.Tickets.Count(),
                e.CreatedAt,
                e.ModifiedAt))
            .ToListAsync(ct);
    }

    public async Task<EpicDto> CreateAsync(CreateEpicRequest request, CancellationToken ct)
    {
        var title = Normalization.Trim(request.Title);
        if (Normalization.IsBlank(title))
            throw ServiceException.Validation("title", "Epic title is required.");
        if (title.Length > FieldLimits.EpicTitleMax)
            throw ServiceException.Validation("title", $"Epic title must be at most {FieldLimits.EpicTitleMax} characters.");

        var description = Normalization.NormalizeOptionalText(request.Description);
        if (description is not null && description.Length > FieldLimits.EpicDescriptionMax)
            throw ServiceException.Validation("description", $"Epic description must be at most {FieldLimits.EpicDescriptionMax} characters.");

        // Missing/unknown team reference in the body => 400 (ADR-0006 rule).
        if (request.TeamId is null || request.TeamId == Guid.Empty)
            throw ServiceException.Validation("teamId", "teamId is required.");
        var teamExists = await _db.Teams.AnyAsync(t => t.Id == request.TeamId, ct);
        if (!teamExists)
            throw ServiceException.Validation("teamId", "The specified team does not exist.");

        var now = _clock.UtcNow;
        var epic = new Epic
        {
            Id = Guid.NewGuid(),
            TeamId = request.TeamId.Value,
            Title = title,
            Description = description,
            CreatedAt = now,
            ModifiedAt = now
        };
        _db.Epics.Add(epic);
        await _db.SaveChangesAsync(ct);

        return new EpicDto(epic.Id, epic.TeamId, epic.Title, epic.Description, 0, epic.CreatedAt, epic.ModifiedAt);
    }

    public async Task<EpicDto> UpdateAsync(Guid id, UpdateEpicRequest request, CancellationToken ct)
    {
        var epic = await _db.Epics.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw ServiceException.NotFound("Epic not found.");

        var title = Normalization.Trim(request.Title);
        if (Normalization.IsBlank(title))
            throw ServiceException.Validation("title", "Epic title is required.");
        if (title.Length > FieldLimits.EpicTitleMax)
            throw ServiceException.Validation("title", $"Epic title must be at most {FieldLimits.EpicTitleMax} characters.");

        var description = Normalization.NormalizeOptionalText(request.Description);
        if (description is not null && description.Length > FieldLimits.EpicDescriptionMax)
            throw ServiceException.Validation("description", $"Epic description must be at most {FieldLimits.EpicDescriptionMax} characters.");

        // No-op rule (A14): all normalized values equal stored => no change, no modified bump.
        // Team is read-only on edit (US-EPIC-2) — any teamId in body is ignored.
        var titleChanged = !string.Equals(title, epic.Title, StringComparison.Ordinal);
        var descriptionChanged = !string.Equals(description, epic.Description, StringComparison.Ordinal);

        if (titleChanged || descriptionChanged)
        {
            epic.Title = title;
            epic.Description = description;
            epic.ModifiedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        var ticketCount = await _db.Tickets.CountAsync(t => t.EpicId == epic.Id, ct);
        return new EpicDto(epic.Id, epic.TeamId, epic.Title, epic.Description, ticketCount, epic.CreatedAt, epic.ModifiedAt);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var epic = await _db.Epics.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw ServiceException.NotFound("Epic not found.");

        var referenced = await _db.Tickets.AnyAsync(t => t.EpicId == id, ct);
        if (referenced)
            throw new ServiceException(ServiceErrorCode.EpicReferencedByTickets,
                "Cannot delete an epic that is referenced by tickets. Reassign or remove those tickets first.");

        _db.Epics.Remove(epic);
        await _db.SaveChangesAsync(ct);
    }
}
