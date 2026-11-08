using System.Buffers.Text;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using Golether.Core.Enums;
using Golether.Core.Networking;
using Golether.Transports.WebRtc;
using Golether.Web.Server.Enums;
using Golether.Web.Server.Infrastructure;
using Golether.Web.Server.Options;

namespace Golether.Web.Server.Signal;

/// <summary>
/// Serves the WebSockets of the signalling: a host takes a room, a participant asks for a meeting, and the server
/// passes the descriptions of the WebRTC connection between them until they talk directly. The server never reads
/// what it passes and keeps nothing on disk.
/// </summary>
internal sealed class SignalHub
{
    /// <summary>
    /// The largest content of one <see cref="SignalFrame.SIGNAL"/>, in characters.
    /// </summary>
    public const int MAX_DATA = 12 * 1024;

    /// <summary>
    /// The number of <see cref="SignalFrame.SIGNAL"/> messages one participant may send in a meeting (a description
    /// and a few dozen candidates are enough).
    /// </summary>
    public const int MAX_SIGNALS = 100;

    /// <summary>
    /// The size of a nonce or a meeting number, in bytes.
    /// </summary>
    private const int RANDOM_BYTES = 16;

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly SignalServerOptions _options;

    /// <summary>
    /// The rooms.
    /// </summary>
    private readonly SignalRegistry _rooms;

    /// <summary>
    /// Bounds the new WebSockets of one address.
    /// </summary>
    private readonly AddressRateLimiter _rate;

    /// <summary>
    /// The time source.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The log.
    /// </summary>
    private readonly ILogger<SignalHub> _logger;

    /// <summary>
    /// The number of open WebSockets.
    /// </summary>
    private int _open;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalHub"/> class.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <param name="rooms">The rooms.</param>
    /// <param name="timeProvider">The time source.</param>
    /// <param name="logger">The log.</param>
    public SignalHub(SignalServerOptions options, SignalRegistry rooms, TimeProvider timeProvider, ILogger<SignalHub> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rate = new AddressRateLimiter(options.MaxConnectionsPerMinute, timeProvider);
    }

    /// <summary>
    /// Decides whether a new WebSocket may be opened: the page is allowed, the address has not opened too many, and
    /// the server has room for one more.
    /// </summary>
    /// <param name="origin">The origin the browser sent.</param>
    /// <param name="remote">The address of the device.</param>
    /// <returns>The HTTP status to answer with, or <see langword="null"/> when the WebSocket may be opened.</returns>
    public int? Admit(string? origin, IPAddress? remote)
    {
        if (!_options.IsAllowedOrigin(origin))
        {
            return StatusCodes.Status403Forbidden;
        }

        if (remote is null || !_rate.TryAcquire(remote) || Volatile.Read(ref _open) >= _options.MaxConnections)
        {
            return StatusCodes.Status429TooManyRequests;
        }

        return null;
    }

    /// <summary>
    /// Serves one WebSocket until it closes.
    /// </summary>
    /// <param name="webSocket">The accepted WebSocket.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>A task that completes when the connection is over.</returns>
    public async Task ServeAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _open);
        using var socket = new SignalSocket(webSocket);
        try
        {
            var nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RANDOM_BYTES));
            await socket.SendAsync(new SignalFrame { Type = SignalFrame.SERVER_HELLO, Version = SignalFrame.PROTOCOL_VERSION, Nonce = nonce }, cancellationToken).ConfigureAwait(false);

            SignalFrame? first;
            using (var limit = new CancellationTokenSource(_options.FirstMessageTimeout, _timeProvider))
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
                first = await socket.ReceiveAsync(wait.Token).ConfigureAwait(false);
            }

            switch (first?.Type)
            {
                case SignalFrame.REGISTER:
                {
                    await ServeHostAsync(socket, first, nonce, cancellationToken).ConfigureAwait(false);
                    break;
                }

                case SignalFrame.CONNECT:
                {
                    await ServeParticipantAsync(socket, first, cancellationToken).ConfigureAwait(false);
                    break;
                }

                case null:
                {
                    break;
                }

                default:
                {
                    await FailAsync(socket, RendezvousError.BadRequest, cancellationToken).ConfigureAwait(false);
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or WebSocketException or OperationCanceledException)
        {
            _logger.LogDebug("A signalling connection ended: {Reason}", ex.Message);
        }
        finally
        {
            await socket.CloseAsync().ConfigureAwait(false);
            Interlocked.Decrement(ref _open);
        }
    }

    /// <summary>
    /// Serves a host: checks its registration, holds its room and passes its signals to the participants.
    /// </summary>
    /// <param name="socket">The WebSocket of the host.</param>
    /// <param name="register">The registration.</param>
    /// <param name="nonce">The nonce given to this connection.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>A task that completes when the host left.</returns>
    private async Task ServeHostAsync(SignalSocket socket, SignalFrame register, string nonce, CancellationToken cancellationToken)
    {
        if (!RendezvousPoint.IsValidRoom(register.Room))
        {
            await FailAsync(socket, RendezvousError.BadRequest, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!KeySignature.TryVerify(register.Key, register.Signature, SignalRegistration.Payload(nonce, register.Room), out var owner))
        {
            await FailAsync(socket, RendezvousError.BadSignature, cancellationToken).ConfigureAwait(false);
            return;
        }

        var room = new SignalRoom(register.Room, owner, socket);
        var claim = _rooms.Claim(room, out var replaced);
        if (claim != RoomClaim.Registered)
        {
            await FailAsync(socket, claim == RoomClaim.Taken ? RendezvousError.RoomTaken : RendezvousError.ServerFull, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (replaced is not null)
        {
            await replaced.Host.CloseAsync().ConfigureAwait(false);
        }

        _logger.LogInformation("A browser holds room {Room} for {Owner}", Short(room.Name), owner.ToShortString());
        try
        {
            await socket.SendAsync(new SignalFrame { Type = SignalFrame.REGISTERED }, cancellationToken).ConfigureAwait(false);
            await RelayHostAsync(socket, room, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _rooms.Release(room);
            _logger.LogInformation("Room {Room} was released", Short(room.Name));
        }
    }

    /// <summary>
    /// Passes the signals of a host to the participants of its meetings.
    /// </summary>
    /// <param name="socket">The WebSocket of the host.</param>
    /// <param name="room">The room of the host.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>A task that completes when the host left.</returns>
    private async Task RelayHostAsync(SignalSocket socket, SignalRoom room, CancellationToken cancellationToken)
    {
        while (await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false) is { } frame)
        {
            if (frame.Type != SignalFrame.SIGNAL || frame.Data is not { Length: > 0 and <= MAX_DATA })
            {
                await FailAsync(socket, RendezvousError.BadRequest, cancellationToken).ConfigureAwait(false);
                return;
            }

            // A meeting that is over is no mistake of the host: the participant may have left a moment ago.
            if (room.FindMeeting(frame.Meeting) is { } participant)
            {
                await participant.SendAsync(new SignalFrame { Type = SignalFrame.SIGNAL, Data = frame.Data }, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Serves a participant: opens a meeting with the host of the room and passes the signals both ways until the
    /// participant leaves or the meeting runs out of time.
    /// </summary>
    /// <param name="socket">The WebSocket of the participant.</param>
    /// <param name="connect">The request.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>A task that completes when the meeting is over.</returns>
    private async Task ServeParticipantAsync(SignalSocket socket, SignalFrame connect, CancellationToken cancellationToken)
    {
        if (!RendezvousPoint.IsValidRoom(connect.Room))
        {
            await FailAsync(socket, RendezvousError.BadRequest, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_rooms.Find(connect.Room) is not { } room)
        {
            await FailAsync(socket, RendezvousError.RoomNotFound, cancellationToken).ConfigureAwait(false);
            return;
        }

        var meeting = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RANDOM_BYTES));
        if (!room.TryAddMeeting(meeting, socket, _options.MaxMeetingsPerRoom))
        {
            await FailAsync(socket, RendezvousError.Busy, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await socket.SendAsync(new SignalFrame { Type = SignalFrame.MEETING, Meeting = meeting }, cancellationToken).ConfigureAwait(false);
            await room.Host.SendAsync(new SignalFrame { Type = SignalFrame.INCOMING, Meeting = meeting }, cancellationToken).ConfigureAwait(false);

            using var limit = new CancellationTokenSource(_options.MeetingTimeout, _timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
            await RelayParticipantAsync(socket, room, meeting, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            room.RemoveMeeting(meeting);
            await room.Host.SendAsync(new SignalFrame { Type = SignalFrame.CLOSED, Meeting = meeting }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Passes the signals of a participant to the host.
    /// </summary>
    /// <param name="socket">The WebSocket of the participant.</param>
    /// <param name="room">The room of the host.</param>
    /// <param name="meeting">The meeting.</param>
    /// <param name="cancellationToken">Ends the meeting.</param>
    /// <returns>A task that completes when the participant left.</returns>
    private async Task RelayParticipantAsync(SignalSocket socket, SignalRoom room, string meeting, CancellationToken cancellationToken)
    {
        var count = 0;
        while (await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false) is { } frame)
        {
            if (frame.Type != SignalFrame.SIGNAL || frame.Data is not { Length: > 0 and <= MAX_DATA } || ++count > MAX_SIGNALS)
            {
                await FailAsync(socket, RendezvousError.BadRequest, cancellationToken).ConfigureAwait(false);
                return;
            }

            await room.Host.SendAsync(new SignalFrame { Type = SignalFrame.SIGNAL, Meeting = meeting, Data = frame.Data }, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends a refusal; the caller closes the connection after it.
    /// </summary>
    /// <param name="socket">The WebSocket.</param>
    /// <param name="error">The reason.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the refusal was sent.</returns>
    private static Task FailAsync(SignalSocket socket, RendezvousError error, CancellationToken cancellationToken)
        => socket.SendAsync(new SignalFrame { Type = SignalFrame.ERROR, Error = error.ToString() }, cancellationToken);

    /// <summary>
    /// Shortens a room name for the log: the whole name would let a reader of the log reach the host.
    /// </summary>
    /// <param name="room">The room name.</param>
    /// <returns>Its first characters.</returns>
    private static string Short(string room) => room[..6] + "…";
}
