using System.Collections.Concurrent;
using TicketTracker.Application.Abstractions;

namespace TicketTracker.Tests.Fakes;

/// <summary>
/// In-memory test double for <see cref="IAttachmentStorage"/> (Wave 3, ADR-0018). Keeps blob bytes in a
/// dictionary keyed by the server-generated opaque storage key, so integration tests over SQLite +
/// WebApplicationFactory never touch the real filesystem volume. Registered as a singleton in the test
/// factory so writes survive across the scoped service lifetimes of a request. Tests can inspect the
/// stored keys/bytes (e.g. assert a delete removed the blob, or that the on-disk key is opaque).
/// </summary>
public sealed class InMemoryAttachmentStorage : IAttachmentStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new();

    /// <summary>The storage keys currently holding a blob (for "delete removed the blob" assertions).</summary>
    public IReadOnlyCollection<string> Keys => _blobs.Keys.ToArray();

    public bool Contains(string storageKey) => _blobs.ContainsKey(storageKey);

    public async Task<long> SaveAsync(string storageKey, Stream content, CancellationToken ct)
    {
        // Copy through the caller's (possibly size-capped) stream so an over-cap upload still throws here,
        // exactly as the real filesystem impl would while streaming.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        _blobs[storageKey] = bytes;
        return bytes.LongLength;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        if (!_blobs.TryGetValue(storageKey, out var bytes))
            throw new FileNotFoundException($"No blob for key '{storageKey}'.");
        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        _blobs.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }
}
