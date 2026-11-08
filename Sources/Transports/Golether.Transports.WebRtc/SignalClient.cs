using System.Net.WebSockets;
using System.Text.Json;

namespace Golether.Transports.WebRtc;

/// <summary>
/// One WebSocket connection of an application to the signalling of the rendezvous server: the greeting, then either the
/// room of a host or the meeting of a guest. Everything the server sends is checked like any network input.
/// </summary>
public sealed class SignalClient : IAsyncDisposable
{
    /// <summary>
    /// The longest message accepted, in bytes.
    /// </summary>
    public const int MAX_MESSAGE = 16 * 1024;

    /// <summary>
    /// The longest nonce of a greeting accepted.
    /// </summary>
    private const int MAX_NONCE = 128;

    /// <summary>
    /// How long the server may take to greet.
    /// </summary>
    private static readonly TimeSpan _greetingTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The socket.
    /// </summary>
    private readonly ClientWebSocket _socket;

    /// <summary>
    /// Lets one send through at a time.
    /// </summary>
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalClient"/> class.
    /// </summary>
    /// <param name="socket">The open socket.</param>
    /// <param name="nonce">The nonce of the greeting.</param>
    private SignalClient(ClientWebSocket socket, string nonce)
    {
        _socket = socket;
        Nonce = nonce;
    }

    /// <summary>
    /// Gets the nonce the server greeted with; a host signs it to take a room.
    /// </summary>
    public string Nonce { get; }

    /// <summary>
    /// Opens a connection and waits for the greeting of the server.
    /// </summary>
    /// <param name="address">The signalling.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The greeted connection.</returns>
    /// <exception cref="SignalException">The server was not reached or speaks another version.</exception>
    public static async Task<SignalClient> ConnectAsync(Uri address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        var socket = new ClientWebSocket();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_greetingTimeout);
            await socket.ConnectAsync(address, timeout.Token).ConfigureAwait(false);
            var hello = await ReceiveAsync(socket, timeout.Token).ConfigureAwait(false);
            if (hello is { Type: SignalFrame.ERROR, Error: { Length: > 0 and <= 64 } error })
            {
                throw new SignalException(error);
            }

            if (hello is not { Type: SignalFrame.SERVER_HELLO, Nonce: { Length: > 0 and <= MAX_NONCE } nonce } ||
                hello.Version != SignalFrame.PROTOCOL_VERSION)
            {
                throw new SignalException(SignalException.UNSUPPORTED_VERSION);
            }

            return new SignalClient(socket, nonce);
        }
        catch (Exception ex) when (ex is WebSocketException or InvalidDataException or OperationCanceledException)
        {
            // A timeout of the greeting is the server not answering; a cancellation of the caller stays one.
            socket.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            throw new SignalException(SignalException.UNREACHABLE, ex);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Takes a room for a host, signing the greeting with the device key.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="signer">The device key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the server confirmed the room.</returns>
    /// <exception cref="SignalException">The server refused the room.</exception>
    public async Task RegisterAsync(string room, DeviceSigner signer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signer);
        await SendAsync(new SignalFrame
        {
            Type = SignalFrame.REGISTER,
            Room = room,
            Key = signer.PublicKey,
            Signature = signer.Sign(SignalRegistration.Payload(Nonce, room)),
        }, cancellationToken).ConfigureAwait(false);
        await ExpectAsync(SignalFrame.REGISTERED, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Meets the host of a room as a guest.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the server started the meeting.</returns>
    /// <exception cref="SignalException">The server refused the meeting (<c>RoomNotFound</c>, <c>Busy</c>, …).</exception>
    public async Task MeetAsync(string room, CancellationToken cancellationToken)
    {
        await SendAsync(new SignalFrame { Type = SignalFrame.CONNECT, Room = room }, cancellationToken).ConfigureAwait(false);
        await ExpectAsync(SignalFrame.MEETING, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a frame; a connection that is already gone drops it.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the frame was sent or dropped.</returns>
    public async Task SendAsync(SignalFrame frame, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, SignalFrame.Json);
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (WebSocketException)
        {
            // The server is gone; the reading side notices and ends the connection.
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>
    /// Receives the next frame.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The frame, or <see langword="null"/> when the server closed the connection.</returns>
    /// <exception cref="InvalidDataException">The server sent something that is no frame.</exception>
    public async Task<SignalFrame?> ReceiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ReceiveAsync(_socket, cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
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

        _socket.Dispose();
        _sendGate.Dispose();
    }

    /// <summary>
    /// Reads one frame from a socket.
    /// </summary>
    /// <param name="socket">The socket.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The frame, or <see langword="null"/> when the socket closed.</returns>
    /// <exception cref="InvalidDataException">The message is too long, binary or not JSON.</exception>
    private static async Task<SignalFrame?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[MAX_MESSAGE];
        var length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                throw new InvalidDataException("The message is too long.");
            }

            var result = await socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
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
    /// Waits for a frame of a type; an error frame or a closed connection fails.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the frame came.</returns>
    /// <exception cref="SignalException">The server refused or left.</exception>
    private async Task ExpectAsync(string type, CancellationToken cancellationToken)
    {
        SignalFrame? frame;
        try
        {
            frame = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            throw new SignalException(SignalException.UNREACHABLE, ex);
        }

        if (frame is { Type: SignalFrame.ERROR, Error: { Length: > 0 and <= 64 } error })
        {
            throw new SignalException(error);
        }

        if (frame?.Type != type)
        {
            throw new SignalException(SignalException.UNREACHABLE);
        }
    }
}
