using System.Threading.Channels;

namespace Golether.Transports.WebRtc;

/// <summary>
/// A byte stream over one data channel: what the sessions read and write instead of a TLS stream. The channel is
/// reliable, ordered and already encrypted (DTLS); its messages are cut and glued back here.
/// </summary>
public sealed class DataChannelStream : Stream
{
    /// <summary>
    /// The largest message sent at once: every browser takes it whole.
    /// </summary>
    public const int MAX_MESSAGE = 16 * 1024;

    /// <summary>
    /// The channel.
    /// </summary>
    private readonly IDataChannel _channel;

    /// <summary>
    /// The messages that arrived and were not read yet.
    /// </summary>
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>
    /// The unread rest of the current message.
    /// </summary>
    private ReadOnlyMemory<byte> _pending;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataChannelStream"/> class.
    /// </summary>
    /// <param name="channel">The open channel; it is closed with the stream.</param>
    public DataChannelStream(IDataChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _channel.MessageReceived += OnMessage;
        _channel.Closed += OnClosed;
        if (!_channel.IsOpen)
        {
            _incoming.Writer.TryComplete();
        }
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => _channel.IsOpen;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (_pending.IsEmpty)
        {
            if (!await _incoming.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false) || !_incoming.Reader.TryRead(out var next))
            {
                return 0;
            }

            _pending = next;
        }

        var count = Math.Min(buffer.Length, _pending.Length);
        _pending[..count].CopyTo(buffer);
        _pending = _pending[count..];
        return count;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_channel.IsOpen)
        {
            throw new IOException("The data channel is closed.");
        }

        for (var offset = 0; offset < buffer.Length; offset += MAX_MESSAGE)
        {
            await _channel.SendAsync(buffer.Slice(offset, Math.Min(MAX_MESSAGE, buffer.Length - offset)), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        Detach();
        await _channel.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Detach();
            _ = _channel.DisposeAsync().AsTask();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Takes a message that arrived; text messages belong to the proofs, not to the stream.
    /// </summary>
    /// <param name="sender">The channel.</param>
    /// <param name="message">The message.</param>
    private void OnMessage(object? sender, DataChannelMessage message)
    {
        if (message.Binary is { Length: > 0 } bytes)
        {
            _incoming.Writer.TryWrite(bytes);
        }
    }

    /// <summary>
    /// Ends the stream: what arrived is still read, then the end.
    /// </summary>
    /// <param name="sender">The channel.</param>
    /// <param name="e">Nothing.</param>
    private void OnClosed(object? sender, EventArgs e) => _incoming.Writer.TryComplete();

    /// <summary>
    /// Stops listening to the channel and ends the stream.
    /// </summary>
    private void Detach()
    {
        _channel.MessageReceived -= OnMessage;
        _channel.Closed -= OnClosed;
        _incoming.Writer.TryComplete();
    }
}
