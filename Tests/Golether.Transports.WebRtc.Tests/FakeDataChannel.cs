namespace Golether.Transports.WebRtc.Tests;

/// <summary>
/// One end of two data channels joined in memory: what one sends, the other receives.
/// </summary>
internal sealed class FakeDataChannel : IDataChannel
{
    /// <summary>
    /// The messages that came before the first listener.
    /// </summary>
    private readonly Queue<DataChannelMessage> _early = new();

    /// <summary>
    /// The listeners.
    /// </summary>
    private EventHandler<DataChannelMessage>? _received;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeDataChannel"/> class.
    /// </summary>
    /// <param name="label">The label.</param>
    private FakeDataChannel(string label)
    {
        Label = label;
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

    /// <inheritdoc />
    public string Label { get; }

    /// <inheritdoc />
    public bool IsOpen { get; private set; } = true;

    /// <summary>
    /// Gets the sizes of the binary messages sent.
    /// </summary>
    public List<int> SentSizes { get; } = [];

    /// <summary>
    /// Gets the other end.
    /// </summary>
    public FakeDataChannel Other { get; private set; } = null!;

    /// <summary>
    /// Makes two joined ends.
    /// </summary>
    /// <param name="label">The label of both.</param>
    /// <returns>The two ends.</returns>
    public static (FakeDataChannel A, FakeDataChannel B) Pair(string label)
    {
        var a = new FakeDataChannel(label);
        var b = new FakeDataChannel(label);
        a.Other = b;
        b.Other = a;
        return (a, b);
    }

    /// <inheritdoc />
    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (!IsOpen)
        {
            throw new IOException("closed");
        }

        SentSizes.Add(data.Length);
        Other.Deliver(new DataChannelMessage(data.ToArray(), null));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void SendText(string text)
    {
        if (!IsOpen)
        {
            throw new IOException("closed");
        }

        Other.Deliver(new DataChannelMessage(null, text));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Close();
        Other.Close();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Passes a message to the listeners, or keeps it for the first one.
    /// </summary>
    /// <param name="message">The message.</param>
    private void Deliver(DataChannelMessage message)
    {
        if (_received is { } handlers)
        {
            handlers(this, message);
        }
        else
        {
            _early.Enqueue(message);
        }
    }

    /// <summary>
    /// Closes this end once.
    /// </summary>
    private void Close()
    {
        if (IsOpen)
        {
            IsOpen = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
