namespace TicketTracker.Application.Options;

/// <summary>
/// Attachment storage + upload limits (Wave 3, ADR-0018 / §9.1). Bound from environment in Program.cs.
/// The root is where <c>LocalFileAttachmentStorage</c> writes blobs (mounted from a Docker named
/// volume in production); the byte cap is enforced while streaming (abort + delete partial → 413).
/// </summary>
public sealed class AttachmentOptions
{
    /// <summary>Filesystem root for blob storage (env ATTACHMENTS_ROOT, default the container volume path).</summary>
    public string Root { get; set; } = "/var/lib/tickettracker/attachments";

    /// <summary>Max upload size per file in bytes (env ATTACHMENTS_MAX_BYTES, default 10 MB, [ASSUMPTION W3-ATT-LIMITS]).</summary>
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
}
