using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Events;
using TicketTracker.Application.Options;
using TicketTracker.Application.Validation;
using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Application.Services;

/// <summary>
/// Attachments on tickets (Wave 3, ADR-0018, API_CONTRACT §5.2). Every endpoint is M(team of ticket):
/// resolve the ticket/attachment → its team (404 if absent) then <c>RequireTeamAccess</c> (403) —
/// resolve-then-check ordering (anti-IDOR, §3.3). Upload/delete are team-write; list/download are
/// team-read (§5.1).
/// <para>
/// Upload streams the payload to <see cref="IAttachmentStorage"/> under a server-generated opaque key
/// (never the client filename), enforces the size cap WHILE streaming (abort + delete partial → 413),
/// and validates the content-type against the allowlist by declared type AND a magic-byte sniff (§7.1)
/// → 415 on a denied/spoofed type. It persists metadata, auto-watches the uploader (mirrors comment
/// add), and publishes <c>attachment_added</c> (activity + notification). Delete removes the row FIRST
/// then best-effort the blob (a crash leaves an orphan blob, not a dangling row, §7.1) and publishes
/// <c>attachment_deleted</c> (activity only). User-initiated writes run inside the execution-strategy
/// transaction (Npgsql retry constraint) — mirrors the WAVE2 backbone.
/// </para>
/// </summary>
public sealed class AttachmentService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IDomainEventPublisher _publisher;
    private readonly IAttachmentStorage _storage;
    private readonly AttachmentOptions _options;

    // Enough leading bytes to sniff every allowlisted signature (webp needs 12; office/OLE up to 8).
    private const int SniffHeadBytes = 512;

    public AttachmentService(
        IAppDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        IDomainEventPublisher publisher,
        IAttachmentStorage storage,
        IOptions<AttachmentOptions> options)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _publisher = publisher;
        _storage = storage;
        _options = options.Value;
    }

    /// <summary>
    /// List a ticket's attachments chronologically (GET /api/tickets/{id}/attachments). Team-read:
    /// resolve ticket → 404, RequireTeamAccess → 403.
    /// </summary>
    public async Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid ticketId, CancellationToken ct)
    {
        var teamId = await ResolveTicketTeamAsync(ticketId, ct);
        _currentUser.RequireTeamAccess(teamId);

        return await _db.Attachments.AsNoTracking()
            .Where(a => a.TicketId == ticketId)
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .Select(a => new AttachmentDto(
                a.Id,
                a.TicketId,
                a.OriginalFilename,
                a.ContentType,
                a.SizeBytes,
                a.UploadedBy,
                a.Uploader != null
                    ? (a.Uploader.Name != null && a.Uploader.Name.Trim() != "" ? a.Uploader.Name.Trim() : a.Uploader.Email)
                    : string.Empty,
                a.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Upload a file to a ticket (POST /api/tickets/{id}/attachments). Team-write: resolve ticket → 404,
    /// RequireTeamAccess → 403. Validates + sniffs the content-type (415), streams to storage enforcing
    /// the byte cap (413), persists metadata, auto-watches the uploader, and publishes attachment_added.
    /// </summary>
    public async Task<AttachmentDto> UploadAsync(Guid ticketId, AttachmentUpload upload, CancellationToken ct)
    {
        var teamId = await ResolveTicketTeamAsync(ticketId, ct);
        _currentUser.RequireTeamAccess(teamId);

        if (upload is null || upload.Content is null)
            throw ServiceException.Validation("file", "A file is required.");

        var declaredType = AttachmentPolicy.NormalizeMediaType(upload.ContentType ?? string.Empty);
        if (!AttachmentPolicy.IsAllowed(declaredType))
            throw ServiceException.UnsupportedMediaType("This file type is not allowed.");

        var filename = SanitizeFilename(upload.FileName);

        var uploaderId = _currentUser.RequireUserId();
        var now = _clock.UtcNow;
        var storageKey = GenerateStorageKey(now);

        // Sniff the leading bytes BEFORE committing anything to disk (415 on a spoof/denied payload).
        var head = new byte[SniffHeadBytes];
        var headLen = await ReadUpToAsync(upload.Content, head, ct);
        if (headLen == 0)
            throw ServiceException.Validation("file", "The file is empty.");
        if (!AttachmentPolicy.SniffMatches(declaredType, head.AsSpan(0, headLen)))
            throw ServiceException.UnsupportedMediaType("The file content does not match its declared type.");

        // Stream head + remainder to storage while enforcing the cap (abort + delete partial on overflow).
        long size;
        var headStream = new MemoryStream(head, 0, headLen, writable: false);
        using var combined = new ConcatStream(headStream, upload.Content);
        using var capped = new SizeCappedStream(combined, _options.MaxBytes);
        try
        {
            size = await _storage.SaveAsync(storageKey, capped, ct);
        }
        catch (PayloadTooLargeException)
        {
            await _storage.DeleteAsync(storageKey, ct);
            throw ServiceException.PayloadTooLarge(
                $"The file exceeds the maximum size of {_options.MaxBytes} bytes.");
        }
        catch
        {
            await _storage.DeleteAsync(storageKey, ct);
            throw;
        }

        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            UploadedBy = uploaderId,
            OriginalFilename = filename,
            ContentType = declaredType,
            SizeBytes = size,
            StorageKey = storageKey,
            CreatedAt = now
        };

        try
        {
            await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                _db.Attachments.Add(attachment);
                // Auto-watch the uploader (mirrors comment auto-watch, §6.3), inside the same commit.
                await WatchService.AddWatch(_db, ticketId, uploaderId, now, ct);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
        catch
        {
            // The metadata never committed — do not leave an orphan blob.
            await _storage.DeleteAsync(storageKey, ct);
            throw;
        }

        var uploader = await _db.Users.AsNoTracking()
            .Where(u => u.Id == uploaderId)
            .Select(u => new { u.Email, u.Name })
            .FirstOrDefaultAsync(ct);
        var uploaderEmail = uploader?.Email ?? string.Empty;
        var uploaderName = uploader?.Name;
        var actor = DisplayName(uploaderName, uploaderEmail);

        // Publish AFTER commit (§6.2): attachment_added → activity + notification to watchers (not the actor).
        var summary = EventSummaries.AttachmentAdded(actor, filename);
        await _publisher.PublishAsync(new[]
        {
            new TicketEvent(EventType.AttachmentAdded, ticketId, uploaderId, null, null, summary, summary)
        }, ct);

        return new AttachmentDto(
            attachment.Id, attachment.TicketId, attachment.OriginalFilename, attachment.ContentType,
            attachment.SizeBytes, attachment.UploadedBy, actor, attachment.CreatedAt);
    }

    /// <summary>
    /// Open an attachment for download (GET /api/attachments/{id}). Team-read: resolve the attachment →
    /// its ticket → its team (404 if the attachment is absent, 403 if the caller lacks team access).
    /// Returns the blob stream + the metadata the controller needs to set forced-download headers.
    /// </summary>
    public async Task<AttachmentDownload> OpenForDownloadAsync(Guid attachmentId, CancellationToken ct)
    {
        var meta = await _db.Attachments.AsNoTracking()
            .Where(a => a.Id == attachmentId)
            .Select(a => new { a.Id, a.StorageKey, a.ContentType, a.OriginalFilename, TeamId = a.Ticket!.TeamId })
            .FirstOrDefaultAsync(ct)
            ?? throw ServiceException.NotFound("Attachment not found.");

        _currentUser.RequireTeamAccess(meta.TeamId);

        var stream = await _storage.OpenReadAsync(meta.StorageKey, ct);
        return new AttachmentDownload(stream, meta.ContentType, meta.OriginalFilename);
    }

    /// <summary>
    /// Delete an attachment (DELETE /api/attachments/{id}). Team-write ([ASSUMPTION W3-ATT-DELETE]):
    /// resolve → 404, RequireTeamAccess → 403. Removes the row FIRST (inside the tx) then best-effort the
    /// blob (a crash leaves an orphan blob, not a dangling row, §7.1), and publishes attachment_deleted
    /// (activity only).
    /// </summary>
    public async Task DeleteAsync(Guid attachmentId, CancellationToken ct)
    {
        var attachment = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId, ct)
            ?? throw ServiceException.NotFound("Attachment not found.");

        var teamId = await ResolveTicketTeamAsync(attachment.TicketId, ct);
        _currentUser.RequireTeamAccess(teamId);

        var currentUserId = _currentUser.RequireUserId();
        var ticketId = attachment.TicketId;
        var filename = attachment.OriginalFilename;
        var storageKey = attachment.StorageKey;

        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            _db.Attachments.Remove(attachment);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        // Row is gone; best-effort blob delete (orphan on failure is reaped separately, §7.1).
        await _storage.DeleteAsync(storageKey, ct);

        var actorRow = await _db.Users.AsNoTracking()
            .Where(u => u.Id == currentUserId)
            .Select(u => new { u.Name, u.Email })
            .FirstOrDefaultAsync(ct);
        var actor = DisplayName(actorRow?.Name, actorRow?.Email ?? string.Empty);

        var summary = EventSummaries.AttachmentDeleted(actor, filename);
        await _publisher.PublishAsync(new[]
        {
            new TicketEvent(EventType.AttachmentDeleted, ticketId, currentUserId, null, null, summary, summary)
        }, ct);
    }

    // ----- helpers -----

    /// <summary>Server-side display name rule (§4.2): name?.trim() || email.</summary>
    private static string DisplayName(string? name, string email)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? email : trimmed;
    }

    /// <summary>
    /// Sanitize the client filename for DISPLAY only (never used to build the disk path, §7.1): take the
    /// leaf name, strip path separators + control chars + NUL, collapse to a safe default when empty, and
    /// bound the length to the column max.
    /// </summary>
    private static string SanitizeFilename(string? raw)
    {
        var name = (raw ?? string.Empty).Trim();
        if (name.Length == 0)
            return "file";

        // Take the leaf only — defeat any embedded path.
        var lastSep = name.LastIndexOfAny(new[] { '/', '\\' });
        if (lastSep >= 0)
            name = name[(lastSep + 1)..];

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsControl(ch) || ch == '/' || ch == '\\' || ch == '\0')
                continue;
            sb.Append(ch);
        }

        var cleaned = sb.ToString().Trim().TrimStart('.');
        if (cleaned.Length == 0)
            return "file";
        return cleaned.Length > 260 ? cleaned[..260] : cleaned;
    }

    /// <summary>Date-sharded opaque key (§4.2): {yyyy}/{MM}/{guid:N}. Server-generated, path-traversal-proof.</summary>
    private static string GenerateStorageKey(DateTime now)
        => $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}";

    private async Task<Guid> ResolveTicketTeamAsync(Guid ticketId, CancellationToken ct)
    {
        var teamId = await _db.Tickets.AsNoTracking()
            .Where(t => t.Id == ticketId)
            .Select(t => (Guid?)t.TeamId)
            .FirstOrDefaultAsync(ct);
        return teamId ?? throw ServiceException.NotFound("Ticket not found.");
    }

    /// <summary>Read up to <paramref name="buffer"/>.Length bytes, tolerating short reads. Returns count read.</summary>
    private static async Task<int> ReadUpToAsync(Stream source, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await source.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }
}

/// <summary>
/// A file being uploaded, decoupled from ASP.NET so the service stays HTTP-agnostic. <see cref="Content"/>
/// is a forward-only stream the service reads once (the controller supplies the multipart section body).
/// </summary>
public sealed record AttachmentUpload(Stream Content, string? FileName, string? ContentType);

/// <summary>A blob opened for download plus the metadata the controller needs for forced-download headers.</summary>
public sealed record AttachmentDownload(Stream Content, string ContentType, string FileName);
