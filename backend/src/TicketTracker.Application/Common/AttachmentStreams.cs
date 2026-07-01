namespace TicketTracker.Application.Common;

/// <summary>
/// Signals that a streamed upload exceeded its byte cap. Caught by <c>AttachmentService.UploadAsync</c>,
/// which deletes the partial blob and maps it to <c>413 payload_too_large</c> (§7.1). Kept internal to the
/// streaming layer so callers deal in <see cref="ServiceException"/>, not this.
/// </summary>
public sealed class PayloadTooLargeException : Exception
{
    public PayloadTooLargeException(string message) : base(message) { }
}

/// <summary>
/// A forward-only read wrapper that throws <see cref="PayloadTooLargeException"/> as soon as the total
/// bytes read exceed <see cref="_maxBytes"/> — so an oversized upload is aborted WHILE streaming, never
/// fully buffered (R-A12). The underlying stream is not disposed by this wrapper (the caller owns it).
/// </summary>
public sealed class SizeCappedStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private long _read;

    public SizeCappedStream(Stream inner, long maxBytes)
    {
        _inner = inner;
        _maxBytes = maxBytes;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = _inner.Read(buffer, offset, count);
        Track(n);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var n = await _inner.ReadAsync(buffer, ct);
        Track(n);
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    private void Track(int n)
    {
        _read += n;
        if (_read > _maxBytes)
            throw new PayloadTooLargeException($"Upload exceeded the maximum of {_maxBytes} bytes.");
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// A forward-only read stream that yields <c>first</c> then <c>second</c> back-to-back. Used to re-join the
/// sniff-head (already read into memory) with the remainder of the upload so the whole payload streams to
/// storage without re-reading or full-buffering. Disposes both underlying streams.
/// </summary>
public sealed class ConcatStream : Stream
{
    private readonly Stream _first;
    private readonly Stream _second;
    private bool _firstDone;

    public ConcatStream(Stream first, Stream second)
    {
        _first = first;
        _second = second;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (!_firstDone)
        {
            var n = await _first.ReadAsync(buffer, ct);
            if (n > 0)
                return n;
            _firstDone = true;
        }
        return await _second.ReadAsync(buffer, ct);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (!_firstDone)
        {
            var n = _first.Read(buffer, offset, count);
            if (n > 0)
                return n;
            _firstDone = true;
        }
        return _second.Read(buffer, offset, count);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _first.Dispose();
            _second.Dispose();
        }
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
