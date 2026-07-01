namespace TicketTracker.Domain.Entities;

/// <summary>
/// A file attached to a ticket (Wave 3, ADR-0018). Only metadata lives in the DB; the blob itself is
/// stored on a local named volume keyed by <see cref="StorageKey"/> (the S3-swap seam is
/// <c>IAttachmentStorage</c>). The on-disk name is the server-generated opaque <see cref="StorageKey"/>,
/// never the client filename (path-traversal defense). <see cref="OriginalFilename"/> is display-only,
/// sanitized, and emitted solely in the download's <c>Content-Disposition</c>. Deleting a ticket
/// cascades its attachment rows away (metadata); the blob cleanup is a service concern (§7.1).
/// Uploading raises <c>attachment_added</c> (activity + notification); deleting raises
/// <c>attachment_deleted</c> (activity only) — like a comment add/delete (ADR-0018).
/// </summary>
public class Attachment
{
    public Guid Id { get; set; }

    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    /// <summary>The user who uploaded the file (RESTRICT — preserve "who uploaded" integrity).</summary>
    public Guid UploadedBy { get; set; }
    public User? Uploader { get; set; }

    /// <summary>Sanitized display name only (strip path separators, control chars); never a disk path.</summary>
    public string OriginalFilename { get; set; } = string.Empty;

    /// <summary>Validated against the allowlist (declared type AND magic-byte sniff, §7.1).</summary>
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>Server-generated opaque key (e.g. <c>{yyyy}/{MM}/{guid}</c>); the on-disk filename. UNIQUE.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
