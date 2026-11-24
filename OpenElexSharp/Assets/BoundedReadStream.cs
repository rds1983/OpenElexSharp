namespace OpenElexSharp.Assets;

/// <summary>A read-only, seekable window over another stream.</summary>
internal sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _start;
    private readonly long _length;
    private long _position;

    public BoundedReadStream(Stream inner, long start, long length)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (!inner.CanRead || !inner.CanSeek)
        {
            throw new ArgumentException("The inner stream must be readable and seekable.", nameof(inner));
        }

        if (start < 0 || length < 0 || start > inner.Length || length > inner.Length - start)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The window lies outside the inner stream.");
        }

        _inner = inner;
        _start = start;
        _length = length;
        _inner.Position = start;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count)
        {
            throw new ArgumentException("The buffer is too small.", nameof(buffer));
        }

        var remaining = _length - _position;
        if (remaining <= 0)
        {
            return 0;
        }

        var toRead = (int)Math.Min(count, remaining);
        _inner.Position = _start + _position;
        var read = _inner.Read(buffer, offset, toRead);
        _position += read;
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var remaining = _length - _position;
        if (remaining <= 0)
        {
            return 0;
        }

        var toRead = (int)Math.Min(buffer.Length, remaining);
        _inner.Position = _start + _position;
        var read = _inner.Read(buffer[..toRead]);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        if (newPosition < 0 || newPosition > _length)
        {
            throw new IOException("Attempted to seek outside the stream window.");
        }

        _position = newPosition;
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
