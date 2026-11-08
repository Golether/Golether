using Golether.Browser.Interop;

namespace Golether.Browser.Media;

/// <summary>
/// A file the user chose on the page, read in place through the browser: the host serves its pieces from here.
/// </summary>
internal sealed class BrowserFileStream : Stream
{
    /// <summary>
    /// The longest part read at once.
    /// </summary>
    private const int MAX_READ = 4 * 1024 * 1024;

    /// <summary>
    /// The number of the file on the page.
    /// </summary>
    private readonly int _file;

    /// <summary>
    /// The current position.
    /// </summary>
    private long _position;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserFileStream"/> class.
    /// </summary>
    /// <param name="file">The number of the file on the page.</param>
    /// <param name="length">The size of the file.</param>
    public BrowserFileStream(int file, long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _file = file;
        Length = length;
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length { get; }

    /// <inheritdoc />
    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = (int)Math.Min(Math.Min(buffer.Length, MAX_READ), Math.Max(0, Length - _position));
        if (count == 0)
        {
            return 0;
        }

        using var bytes = await FileInterop.ReadAsync(_file, _position, count).ConfigureAwait(false);
        var read = FileInterop.Copy(bytes, buffer.Span[..count]);

        _position += read;
        return read;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("A browser reads only asynchronously.");

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => Length + offset,
        };
        return _position;
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
