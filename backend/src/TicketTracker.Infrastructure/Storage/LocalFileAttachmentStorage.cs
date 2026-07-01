using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Options;

namespace TicketTracker.Infrastructure.Storage;

/// <summary>
/// Local-filesystem implementation of <see cref="IAttachmentStorage"/> (Wave 3, ADR-0018). Blobs live
/// under <see cref="AttachmentOptions.Root"/> (a Docker named volume in production), one file per
/// server-generated opaque <c>storageKey</c>. The root is created on first use (the container must run
/// with a uid that can write the mounted volume, §9.1).
/// <para>
/// <b>Path-traversal defense (§7.1):</b> the final path is <c>Path.Combine(root, storageKey)</c> with a
/// post-combine assertion that the fully-resolved absolute path is still UNDER the resolved root — any
/// key that escapes (via <c>..</c>, an absolute path, or a rooted drive) is rejected. Keys are always
/// server-generated so this is defense in depth against a key-generation bug, never client input.
/// </para>
/// </summary>
public sealed class LocalFileAttachmentStorage : IAttachmentStorage
{
    private readonly string _root;
    private readonly ILogger<LocalFileAttachmentStorage> _logger;

    public LocalFileAttachmentStorage(IOptions<AttachmentOptions> options, ILogger<LocalFileAttachmentStorage> logger)
    {
        _root = Path.GetFullPath(options.Value.Root);
        _logger = logger;
        Directory.CreateDirectory(_root);
    }

    public async Task<long> SaveAsync(string storageKey, Stream content, CancellationToken ct)
    {
        var path = ResolveUnderRoot(storageKey);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, ct);
        await file.FlushAsync(ct);
        return file.Length;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        var path = ResolveUnderRoot(storageKey);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        try
        {
            var path = ResolveUnderRoot(storageKey);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            // Best-effort: a failed blob delete leaves an orphan (reaped separately, §7.1) but must not
            // fail the user's delete — the metadata row is already gone.
            _logger.LogWarning(ex, "Failed to delete attachment blob for key {StorageKey}.", storageKey);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolve <paramref name="storageKey"/> to an absolute path and assert it stays under the storage root.
    /// Throws <see cref="InvalidOperationException"/> on any traversal attempt (defense in depth, §7.1).
    /// </summary>
    private string ResolveUnderRoot(string storageKey)
    {
        var combined = Path.GetFullPath(Path.Combine(_root, storageKey));
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSep, StringComparison.Ordinal))
            throw new InvalidOperationException("Resolved attachment path escapes the storage root.");

        return combined;
    }
}
