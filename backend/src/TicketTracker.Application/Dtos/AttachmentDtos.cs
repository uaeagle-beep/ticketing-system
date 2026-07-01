namespace TicketTracker.Application.Dtos;

// API_CONTRACT §5.2 (Wave 3, ADR-0018). Attachments are ticket sub-resources; team-scoped access.

/// <summary>
/// Attachment metadata as returned by the list/upload endpoints. Never carries the storage key (that is
/// a server-internal opaque disk name, §7.1). <c>UploadedByDisplayName = name?.trim() || email</c> is
/// computed server-side; the SPA never recomputes it.
/// </summary>
public sealed record AttachmentDto(
    Guid Id,
    Guid TicketId,
    string Filename,
    string ContentType,
    long SizeBytes,
    Guid UploadedBy,
    string UploadedByDisplayName,
    DateTime CreatedAt);
