using System.Net.WebSockets;
using System.Text.Json;
using Golether.Transports.WebRtc;

namespace Golether.Web.Server.Signal;

/// <summary>
/// A WebSocket of the signalling: whole JSON messages of a bounded size in, and sends from several tasks one at a
/// time out.
/// </summary>
internal sealed class SignalSocket : IDisposable
{
    /// <summary>
    /// The largest message accepted, in bytes.
    /// </summary>
    public const int MAX_MESSAGE = 16 * 1024;

    /// <summary>
    /// The WebSocket.
    /// </summary>
    private readonly WebSocket _socket;

    /// <summary>
    /// Lets one send through at a time: a WebSocket takes no parallel sends.
    /// </summary>
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <summary>
    /// Set once the socket was released: a late send of another connection is then dropped.
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalSocket"/> class.
    /// </summary>
    /// <param name="socket">The accepted WebSocket.</param>
    public SignalSocket(WebSocket socket)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
    }

    /// <summary>
    /// Sends a message. A socket that is already closed or released is not an error: the other side left (a meeting
    /// may still pass a signal of its guest to a host that is gone).
    /// </summary>
    /// <param name="frame">The message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the message was sent or dropped.</returns>
    public async Task SendAsync(SignalFrame frame, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, SignalFrame.Json);
        if (_disposed)
        {
            return;
        }

        try
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (!_disposed && _socket.State == WebSocketState.Open)
            {
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
        {
            // The other side vanished; its own loop ends the connection.
        }
        finally
        {
            ReleaseGate();
        }
    }

    /// <summary>
    /// Receives the next message.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The message, or <see langword="null"/> when the other side closed the socket.</returns>
    /// <exception cref="InvalidDataException">The message is too long, binary or not JSON.</exception>
    public async Task<SignalFrame?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[MAX_MESSAGE];
        var length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                throw new InvalidDataException("The message is too long.");
            }

            var result = await _socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidDataException("Only text messages are accepted.");
            }

            length += result.Count;
            if (result.EndOfMessage)
            {
                break;
            }
        }

        try
        {
            return JsonSerializer.Deserialize<SignalFrame>(buffer.AsSpan(0, length), SignalFrame.Json)
                ?? throw new InvalidDataException("The message is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The message is not valid JSON.", ex);
        }
    }

    /// <summary>
    /// Closes the socket politely; a socket that is already gone is left alone.
    /// </summary>
    /// <returns>A task that completes when the socket was closed.</returns>
    public async Task CloseAsync()
    {
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // Nothing is left to close.
        }
    }

    /// <summary>
    /// Releases the send gate; later sends are dropped.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _sendGate.Dispose();
    }

    /// <summary>
    /// Lets the next send through, unless the socket was released meanwhile.
    /// </summary>
    private void ReleaseGate()
    {
        try
        {
            _sendGate.Release();
        }
        catch (ObjectDisposedException)
        {
            // Released while sending: nobody waits any more.
        }
    }
}
