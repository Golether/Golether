using System.Runtime.InteropServices;
using Golether.Browser.Interop;
using Golether.Transports.WebRtc;

namespace Golether.Browser.Transport;

/// <summary>
/// A data channel of the page (<c>rtc.js</c>) as an <see cref="IDataChannel"/>, so the sessions of a browser read and
/// write it through the same <see cref="DataChannelStream"/> as the applications. Messages that arrive before anybody
/// listens are kept for the first listener.
/// </summary>
/// <remarks>The browser runs .NET on one thread, so no lock is needed.</remarks>
internal sealed class JsDataChannel : IDataChannel
{
    /// <summary>
    /// The most messages kept for a listener that is not there yet.
    /// </summary>
    private const int MAX_EARLY = 256;

    /// <summary>
    /// The messages that came before the first listener.
    /// </summary>
    private readonly Queue<DataChannelMessage> _early = new();

    /// <summary>
    /// The listeners of the messages.
    /// </summary>
    private EventHandler<DataChannelMessage>? _received;

    /// <summary>
    /// Whether the channel is open.
    /// </summary>
    private bool _open = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsDataChannel"/> class.
    /// </summary>
    /// <param name="id">The number of the channel on the page.</param>
    public JsDataChannel(int id)
    {
        Id = id;
    }

    /// <inheritdoc />
    public event EventHandler<DataChannelMessage>? MessageReceived
    {
        add
        {
            _received += value;
            while (_early.TryDequeue(out var message))
            {
                value?.Invoke(this, message);
            }
        }

        remove => _received -= value;
    }

    /// <inheritdoc />
    public event EventHandler? Closed;

    /// <summary>
    /// Gets the number of the channel on the page.
    /// </summary>
    public int Id { get; }

    /// <inheritdoc />
    public string Label => string.Empty;

    /// <inheritdoc />
    public bool IsOpen => _open;

    /// <inheritdoc />
    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_open)
        {
            throw new IOException("The data channel is closed.");
        }

        // The page copies the bytes before returning, so the memory of .NET is only read.
        if (RtcInterop.Send(Id, MemoryMarshal.AsMemory(data).Span))
        {
            await RtcInterop.DrainAsync(Id).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: the channels of the sessions of a browser carry bytes only; the
    /// proofs are exchanged by the page itself.</exception>
    public void SendText(string text) => throw new NotSupportedException("The channels of a browser session carry bytes only.");

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (DataChannels.Forget(Id))
        {
            RtcInterop.Close(Id);
        }

        MarkClosed();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Takes a message that arrived.
    /// </summary>
    /// <param name="data">The bytes.</param>
    public void Deliver(byte[] data)
    {
        var message = new DataChannelMessage(data, null);
        if (_received is { } handlers)
        {
            handlers(this, message);
        }
        else if (_early.Count < MAX_EARLY)
        {
            _early.Enqueue(message);
        }
    }

    /// <summary>
    /// Marks the channel closed by either side.
    /// </summary>
    public void MarkClosed()
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
