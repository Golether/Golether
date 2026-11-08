using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Transports.WebRtc;

/// <summary>
/// The way into a hosted session through the signalling: the host keeps its room there, signed with the device key,
/// and every guest that meets it gets a WebRTC connection of its own (<see cref="WebRtcHostLink"/>). The room is taken
/// again whenever the connection with the server breaks.
/// </summary>
public sealed class WebRtcSessionHost : IAsyncDisposable
{
    /// <summary>
    /// The first pause before the room is taken again, in seconds.
    /// </summary>
    private const int FIRST_RETRY_SECONDS = 3;

    /// <summary>
    /// The longest pause before the room is taken again, in seconds.
    /// </summary>
    private const int MAX_RETRY_SECONDS = 30;

    /// <summary>
    /// The signalling.
    /// </summary>
    private readonly Uri _signal;

    /// <summary>
    /// The room.
    /// </summary>
    private readonly string _room;

    /// <summary>
    /// The device key of the host.
    /// </summary>
    private readonly DeviceSigner _signer;

    /// <summary>
    /// Makes the connections.
    /// </summary>
    private readonly IDataLinkFactory _factory;

    /// <summary>
    /// Takes the streams of the guests.
    /// </summary>
    private readonly Func<PeerStream, bool> _offer;

    /// <summary>
    /// The STUN server of the connections, or <see langword="null"/>.
    /// </summary>
    private readonly string? _stunServer;

    /// <summary>
    /// The limits.
    /// </summary>
    private readonly WebRtcOptions _options;

    /// <summary>
    /// The source of time.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// The log.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Stops the work.
    /// </summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    /// Guards the connections.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The connections by meeting while their meeting lasts.
    /// </summary>
    private readonly Dictionary<string, WebRtcHostLink> _meetings = [];

    /// <summary>
    /// Every connection alive, met or proven.
    /// </summary>
    private readonly HashSet<WebRtcHostLink> _alive = [];

    /// <summary>
    /// The loop that keeps the room.
    /// </summary>
    private Task _loop = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRtcSessionHost"/> class.
    /// </summary>
    /// <param name="signal">The signalling.</param>
    /// <param name="room">The room, the same as on the rendezvous server.</param>
    /// <param name="signer">The device key of the host.</param>
    /// <param name="links">Makes the connections.</param>
    /// <param name="offer">Takes a stream of a proven guest; <see langword="false"/> when the session refuses it.</param>
    /// <param name="stunServer">The STUN server (<c>stun://host:port</c>), or <see langword="null"/>.</param>
    /// <param name="options">The limits, or <see langword="null"/> for the defaults.</param>
    /// <param name="timeProvider">The source of time.</param>
    /// <param name="logger">The log.</param>
    public WebRtcSessionHost(
        Uri signal,
        string room,
        DeviceSigner signer,
        IDataLinkFactory links,
        Func<PeerStream, bool> offer,
        string? stunServer = null,
        WebRtcOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<WebRtcSessionHost>? logger = null)
    {
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        _room = room ?? throw new ArgumentNullException(nameof(room));
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _factory = links ?? throw new ArgumentNullException(nameof(links));
        _offer = offer ?? throw new ArgumentNullException(nameof(offer));
        _stunServer = stunServer;
        _options = options ?? new WebRtcOptions();
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<WebRtcSessionHost>.Instance;
    }

    /// <summary>
    /// Raised when <see cref="IsRegistered"/> or <see cref="LastError"/> change.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Gets a value indicating whether the room is held now.
    /// </summary>
    public bool IsRegistered { get; private set; }

    /// <summary>
    /// Gets why the room was lost or not taken, or <see langword="null"/>.
    /// </summary>
    public Exception? LastError { get; private set; }

    /// <summary>
    /// Starts keeping the room.
    /// </summary>
    public void Start() => _loop = Task.Run(() => RunAsync(_stop.Token));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }

        WebRtcHostLink[] links;
        lock (_gate)
        {
            links = [.. _alive];
            _alive.Clear();
            _meetings.Clear();
        }

        foreach (var link in links)
        {
            await link.DisposeAsync().ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    /// <summary>
    /// Keeps the room: takes it, serves the meetings, and takes it again after a pause when the server is lost.
    /// </summary>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when the work is stopped.</returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await SignalClient.ConnectAsync(_signal, cancellationToken).ConfigureAwait(false);
                await using (client.ConfigureAwait(false))
                {
                    await client.RegisterAsync(_room, _signer, cancellationToken).ConfigureAwait(false);
                    attempt = 0;
                    SetState(true, null);
                    await ServeAsync(client, cancellationToken).ConfigureAwait(false);
                    SetState(false, new SignalException(SignalException.UNREACHABLE));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                SetState(false, ex);
                _logger.LogWarning("The room for browsers is not held: {Reason}", ex.Message);
            }

            var delay = TimeSpan.FromSeconds(Math.Min(MAX_RETRY_SECONDS, FIRST_RETRY_SECONDS << Math.Min(attempt++, 4)));
            await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Serves the frames of the server while the connection lasts.
    /// </summary>
    /// <param name="client">The connection with the server.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when the server is lost.</returns>
    private async Task ServeAsync(SignalClient client, CancellationToken cancellationToken)
    {
        while (await client.ReceiveAsync(cancellationToken).ConfigureAwait(false) is { } frame)
        {
            switch (frame.Type)
            {
                case SignalFrame.INCOMING when frame.Meeting is { Length: > 0 and <= 64 } meeting:
                    Accept(client, meeting);
                    break;
                case SignalFrame.SIGNAL when Find(frame.Meeting) is { } link:
                    _ = link.OnSignalAsync(frame.Data);
                    break;
                case SignalFrame.CLOSED when frame.Meeting is { } meeting:
                    EndMeeting(meeting);
                    break;
                default:
                    // An error frame or one this side does not know is no reason to lose the room.
                    break;
            }
        }
    }

    /// <summary>
    /// Starts a connection for a new meeting, unless too many are not proven yet.
    /// </summary>
    /// <param name="client">The connection with the server.</param>
    /// <param name="meeting">The meeting.</param>
    private void Accept(SignalClient client, string meeting)
    {
        lock (_gate)
        {
            if (_meetings.ContainsKey(meeting) || _alive.Count(l => !l.IsProven) >= _options.MaxPendingMeetings)
            {
                return;
            }
        }

        var link = new WebRtcHostLink(
            _factory.Create(_stunServer, null),
            _room,
            _signer,
            _offer,
            data => client.SendAsync(new SignalFrame { Type = SignalFrame.SIGNAL, Meeting = meeting, Data = data }, CancellationToken.None),
            _options,
            _time,
            _logger);
        link.Ended += (_, _) => Forget(meeting, link);
        lock (_gate)
        {
            _meetings[meeting] = link;
            _alive.Add(link);
        }
    }

    /// <summary>
    /// Finds the connection of a meeting.
    /// </summary>
    /// <param name="meeting">The meeting.</param>
    /// <returns>The connection, or <see langword="null"/>.</returns>
    private WebRtcHostLink? Find(string? meeting)
    {
        if (meeting is null)
        {
            return null;
        }

        lock (_gate)
        {
            return _meetings.GetValueOrDefault(meeting);
        }
    }

    /// <summary>
    /// Ends a meeting: a connection that is not proven by now is closed; a proven one lives on without the server.
    /// </summary>
    /// <param name="meeting">The meeting.</param>
    private void EndMeeting(string meeting)
    {
        WebRtcHostLink? link;
        lock (_gate)
        {
            _meetings.Remove(meeting, out link);
        }

        if (link is { IsProven: false })
        {
            _ = link.DisposeAsync().AsTask();
        }
    }

    /// <summary>
    /// Forgets a connection that closed.
    /// </summary>
    /// <param name="meeting">Its meeting.</param>
    /// <param name="link">The connection.</param>
    private void Forget(string meeting, WebRtcHostLink link)
    {
        lock (_gate)
        {
            if (_meetings.TryGetValue(meeting, out var current) && current == link)
            {
                _meetings.Remove(meeting);
            }

            _alive.Remove(link);
        }
    }

    /// <summary>
    /// Records the state of the room and tells about a change.
    /// </summary>
    /// <param name="registered">Whether the room is held.</param>
    /// <param name="error">Why it is not, or <see langword="null"/>.</param>
    private void SetState(bool registered, Exception? error)
    {
        if (IsRegistered == registered && ReferenceEquals(LastError, error))
        {
            return;
        }

        IsRegistered = registered;
        LastError = error;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
