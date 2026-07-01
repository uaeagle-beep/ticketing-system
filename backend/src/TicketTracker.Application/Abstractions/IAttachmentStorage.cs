namespace TicketTracker.Application.Abstractions;

/// <summary>
/// Blob storage port for attachment files (Wave 3, ADR-0018). This is the S3-swap seam: production
/// binds a local-filesystem implementation storing blobs under a configured root keyed by a
/// server-generated opaque <c>storageKey</c>; tests bind an in-memory/temp-dir implementation so
/// integration tests over SQLite never touch the real volume. A future move to S3/MinIO is a new
/// binding — no service/contract change.
/// <para>
/// The <c>storageKey</c> is always server-generated and opaque (never a client path); implementations
/// MUST treat it as trusted-but-verify and refuse any key that would resolve outside the storage root
/// (path-traversal defense in depth, §7.1).
/// </para>
/// </summary>
public interface IAttachmentStorage
{
    /// <summary>
    /// Persist the given content under the supplied opaque <paramref name="storageKey"/>, streaming from
    /// <paramref name="content"/> (never full-buffering). Returns the number of bytes written. The caller
    /// generates the key and is responsible for enforcing the size cap while producing the stream.
    /// </summary>
    Task<long> SaveAsync(string storageKey, Stream content, CancellationToken ct);

    /// <summary>Open the blob for reading. Throws if the key is unknown. The caller disposes the stream.</summary>
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct);

    /// <summary>Best-effort delete of the blob. A missing blob is not an error (idempotent, §7.1 orphan note).</summary>
    Task DeleteAsync(string storageKey, CancellationToken ct);
}
