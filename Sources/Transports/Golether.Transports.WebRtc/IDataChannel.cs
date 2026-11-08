namespace Golether.Transports.WebRtc;

/// <summary>
/// A reliable, ordered data channel of a verified WebRTC connection.
/// </summary>
public interface IDataChannel : IAsyncDisposable
{
    /// <summary>
    /// Raised for every message that arrives. Messages that arrive before the first handler is attached wait for it,
    /// so the first message of the other side is not lost to the moment the channel is handed over.
    /// </summary>
    event EventHandler<DataChannelMessage>? MessageReceived;

    /// <summary>
    /// Raised once when the channel closes, from either side.
    /// </summary>
    event EventHandler? Closed;

    /// <summary>
    /// Gets the label the channel was opened with.
    /// </summary>
    string Label { get; }

    /// <summary>
    /// Gets a value indicating whether the channel is open.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Sends a binary message; waits while too much is unsent.
    /// </summary>
    /// <param name="data">The message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the message was handed to the connection.</returns>
    /// <exception cref="IOException">The channel is closed.</exception>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a text message.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <exception cref="IOException">The channel is closed.</exception>
    void SendText(string text);
}
