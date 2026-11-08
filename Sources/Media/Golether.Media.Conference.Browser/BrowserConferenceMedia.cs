using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Media.Conference.Browser.Enums;

namespace Golether.Media.Conference.Browser;

/// <summary>
/// Cameras, voice and the data channel of a browser: the <see cref="IConferenceMedia"/> the sessions drive, with the
/// same rules as the applications (<see cref="SignalRules"/>, <see cref="ConnectionRules"/>,
/// <see cref="VideoQualityPolicy"/>, <see cref="VoiceActivityDetector"/>). The decisions are made here; the page only
/// carries them out (<see cref="IBrowserRtc"/>) and reports back through the <c>On…</c> methods.
/// </summary>
/// <remarks>
/// The browser itself refuses a DTLS certificate that does not match the fingerprint of the SDP, and the SDP arrives
/// only over the authenticated channel of the session; no track and no data channel opens before that check, so
/// <see cref="PeerLinkState.Connected"/> means the other side is the participant the session named.
/// </remarks>
public sealed class BrowserConferenceMedia : IConferenceMedia
{
    /// <summary>
    /// How often the page reports the levels of the voices.
    /// </summary>
    public static TimeSpan LevelPeriod { get; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The level given to silence, in dBFS.
    /// </summary>
    private const double SILENCE_DB = -120;

    /// <summary>
    /// How often the speaking state of participants who went quiet is ended.
    /// </summary>
    private static readonly TimeSpan _voiceExpiry = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// The browser.
    /// </summary>
    private readonly IBrowserRtc _rtc;

    /// <summary>
    /// The STUN server used when the session names none, in the form of the browser.
    /// </summary>
    private readonly string _defaultStun;

    /// <summary>
    /// The log.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The source of time.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// The moment the voice detector counts from.
    /// </summary>
    private readonly long _start;

    /// <summary>
    /// Guards the table of connections and the device state.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The connections by participant.
    /// </summary>
    private readonly Dictionary<PeerId, BrowserPeer> _peers = [];

    /// <summary>
    /// The loudness of the voices set by the user, by participant.
    /// </summary>
    private readonly Dictionary<PeerId, double> _volumes = [];

    /// <summary>
    /// Lets one change of the devices run at a time: the browser asks for a permission once.
    /// </summary>
    private readonly SemaphoreSlim _devices = new(1, 1);

    /// <summary>
    /// Decides who is speaking from the levels of the voices.
    /// </summary>
    private readonly VoiceActivityDetector _voices = new();

    /// <summary>
    /// The STUN server of the session in the form of the browser.
    /// </summary>
    private string _stun;

    /// <summary>
    /// The relay of the host the cameras use when there is no direct path, or <see langword="null"/>.
    /// </summary>
    private BrowserIceServer? _relay;

    /// <summary>
    /// The chosen camera, or <see langword="null"/> for the default one.
    /// </summary>
    private string? _cameraId;

    /// <summary>
    /// The chosen microphone, or <see langword="null"/> for the default one.
    /// </summary>
    private string? _microphoneId;

    /// <summary>
    /// Whether the microphone is switched off.
    /// </summary>
    private bool _microphoneMuted;

    /// <summary>
    /// Whether the camera is switched off.
    /// </summary>
    private bool _cameraOff;

    /// <summary>
    /// Whether the capture was started by the session.
    /// </summary>
    private bool _started;

    /// <summary>
    /// Ends the speaking state of participants who went quiet, while the capture runs.
    /// </summary>
    private ITimer? _voiceTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserConferenceMedia"/> class.
    /// </summary>
    /// <param name="rtc">The browser.</param>
    /// <param name="defaultStun">The STUN server used when the session names none (<c>stun:host:port</c>).</param>
    /// <param name="logger">The log.</param>
    /// <param name="timeProvider">The source of time.</param>
    public BrowserConferenceMedia(IBrowserRtc rtc, string defaultStun, ILogger<BrowserConferenceMedia>? logger = null, TimeProvider? timeProvider = null)
    {
        _rtc = rtc ?? throw new ArgumentNullException(nameof(rtc));
        _defaultStun = ToBrowserStun(defaultStun ?? throw new ArgumentNullException(nameof(defaultStun)));
        _stun = _defaultStun;
        _logger = logger ?? NullLogger<BrowserConferenceMedia>.Instance;
        _time = timeProvider ?? TimeProvider.System;
        _start = _time.GetTimestamp();
    }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public string? UnavailableReason => null;

    /// <inheritdoc />
    /// <remarks>Never raised: the page shows the pictures of the participants itself.</remarks>
    public event EventHandler<ParticipantVideoFrame>? VideoFrameReceived
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <inheritdoc />
    public event EventHandler<ConferenceSignal>? SignalReady;

    /// <inheritdoc />
    public event PeerDataHandler? DataReceived;

    /// <inheritdoc />
    public event EventHandler<SpeakingChange>? SpeakingChanged;

    /// <inheritdoc />
    public event EventHandler<VideoQualityChange>? VideoQualityChanged;

    /// <inheritdoc />
    public event EventHandler? MuteChanged;

    /// <inheritdoc />
    public event EventHandler? MicrophoneFailed;

    /// <inheritdoc />
    public event EventHandler? CameraFailed;

    /// <inheritdoc />
    public IReadOnlyCollection<PeerId> DataPeers
    {
        get
        {
            lock (_gate)
            {
                return [.. _peers.Values.Where(p => p.DataOpen && p.Connected).Select(p => p.Id)];
            }
        }
    }

    /// <inheritdoc />
    public bool MicrophoneMuted
    {
        get
        {
            lock (_gate)
            {
                return _microphoneMuted;
            }
        }
    }

    /// <inheritdoc />
    public bool CameraOff
    {
        get
        {
            lock (_gate)
            {
                return _cameraOff;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<CaptureDevice> GetDevices() => _rtc.ListDevices();

    /// <inheritdoc />
    public IReadOnlyList<ConferencePeerDiagnostics> GetPeerDiagnostics()
    {
        lock (_gate)
        {
            return [.. _peers.Values.Select(p => new ConferencePeerDiagnostics(
                p.Id,
                p.Connected,
                p.DataOpen,
                p.Quality.Quality,
                p.Traffic.VideoSent,
                p.Traffic.VideoReceived,
                p.Traffic.AudioSent,
                p.Traffic.AudioReceived))];
        }
    }

    /// <inheritdoc />
    public async Task StartCaptureAsync(CaptureDevice? camera, CaptureDevice? microphone, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _cameraId = camera?.Id;
            _microphoneId = microphone?.Id;
            if (!_started)
            {
                _started = true;
                _voiceTimer = _time.CreateTimer(_ => ExpireVoices(), null, _voiceExpiry, _voiceExpiry);
            }
        }

        await ApplyDevicesAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void SetMuted(bool microphoneMuted, bool cameraOff)
    {
        bool started;
        lock (_gate)
        {
            if (_microphoneMuted == microphoneMuted && _cameraOff == cameraOff)
            {
                return;
            }

            _microphoneMuted = microphoneMuted;
            _cameraOff = cameraOff;
            started = _started;
        }

        MuteChanged?.Invoke(this, EventArgs.Empty);
        if (microphoneMuted)
        {
            Raise(_voices.Forget(default));
        }

        if (started)
        {
            _ = ApplyDevicesSafelyAsync();
        }
    }

    /// <inheritdoc />
    public bool TrySendData(PeerId peer, ReadOnlySpan<byte> message)
    {
        lock (_gate)
        {
            if (!_peers.TryGetValue(peer, out var known) || !known.DataOpen || !known.Connected)
            {
                return false;
            }
        }

        return _rtc.TrySend(peer, message);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A browser cannot reach the relay of the host through the session; it reaches it directly, over TCP, at the public
    /// address of the host. Connections made later use it.
    /// </remarks>
    public void SetRelay(string? turnServer)
    {
        var relay = turnServer is null ? null : BrowserIceServer.FromTurnUri(turnServer);
        lock (_gate)
        {
            _relay = relay;
        }
    }

    /// <inheritdoc />
    public void SetStunServer(string? stunServer)
    {
        var stun = stunServer is null ? _defaultStun : ToBrowserStun(stunServer);
        lock (_gate)
        {
            _stun = stun;
        }
    }

    /// <inheritdoc />
    public void SetVoiceVolume(PeerId peer, double volume)
    {
        var clamped = PcmGain.Clamp(volume);
        lock (_gate)
        {
            _volumes[peer] = clamped;
        }

        _rtc.SetVolume(peer, clamped);
    }

    /// <inheritdoc />
    public Task ConnectAsync(PeerId peer, bool isOfferer, CancellationToken cancellationToken)
    {
        if (peer.IsEmpty)
        {
            throw new ArgumentException("The participant must be known.", nameof(peer));
        }

        lock (_gate)
        {
            if (_peers.ContainsKey(peer))
            {
                return Task.CompletedTask;
            }
        }

        Open(new BrowserPeer(peer, isOfferer, attempt: 0));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task HandleSignalAsync(ConferenceSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        SignalRules.Validate(signal.Kind, signal.Payload);

        var peer = Accept(signal);
        if (peer is null)
        {
            return;
        }

        if (signal.Kind == SignalKinds.CANDIDATE)
        {
            SignalRules.TryParseCandidate(signal.Payload, out var line, out var candidate);
            _rtc.AddCandidate(peer.Id, line, candidate);
            return;
        }

        try
        {
            await _rtc.ApplyDescriptionAsync(peer.Id, signal.Kind, signal.Payload).WaitAsync(ConnectionRules.NegotiationTimeout, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            Fail(peer, "the description was not applied: " + ex.Message);
        }
    }

    /// <inheritdoc />
    public Task DisconnectAsync(PeerId peer)
    {
        BrowserPeer? removed;
        lock (_gate)
        {
            _peers.Remove(peer, out removed);
            _volumes.Remove(peer);
        }

        if (removed is not null)
        {
            removed.Dispose();
            _rtc.Close(peer);
            _logger.LogInformation("Disconnected from {Peer}", peer.ToShortString());
        }

        Raise(_voices.Forget(peer));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        BrowserPeer[] peers;
        lock (_gate)
        {
            peers = [.. _peers.Values];
            _peers.Clear();
            _volumes.Clear();
            _started = false;
            _voiceTimer?.Dispose();
            _voiceTimer = null;
        }

        foreach (var peer in peers)
        {
            peer.Dispose();
            Raise(_voices.Forget(peer.Id));
        }

        Raise(_voices.Forget(default));
        _rtc.StopAll();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _devices.Dispose();
    }

    /// <summary>
    /// Takes a description or a candidate the page made and passes it to the session.
    /// </summary>
    /// <param name="peer">The participant it is for.</param>
    /// <param name="kind">The signaling kind.</param>
    /// <param name="payload">The payload.</param>
    public void OnSignal(PeerId peer, string kind, string payload)
    {
        lock (_gate)
        {
            if (!_peers.ContainsKey(peer))
            {
                return;
            }
        }

        SignalReady?.Invoke(this, new ConferenceSignal(peer, kind, payload));
    }

    /// <summary>
    /// Takes a change of the state of a connection.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="state">The new state.</param>
    public void OnConnectionState(PeerId peer, PeerLinkState state)
    {
        BrowserPeer? known;
        lock (_gate)
        {
            if (!_peers.TryGetValue(peer, out known))
            {
                return;
            }

            if (state == PeerLinkState.Connected)
            {
                known.Connected = true;
                known.WasConnected = true;
                known.Failing = false;
                known.ConnectTimer?.Dispose();
                known.ConnectTimer = null;
            }
            else
            {
                known.Connected = false;
            }
        }

        if (state == PeerLinkState.Connected)
        {
            _logger.LogInformation("Cameras connected with {Peer}", peer.ToShortString());
        }
        else if (state == PeerLinkState.Failed)
        {
            Fail(known, "the browser gave up the connection");
        }
    }

    /// <summary>
    /// Takes the opening of the data channel of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    public void OnDataOpened(PeerId peer) => SetDataOpen(peer, true);

    /// <summary>
    /// Takes the closing of the data channel of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    public void OnDataClosed(PeerId peer) => SetDataOpen(peer, false);

    /// <summary>
    /// Takes a message of the data channel of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="data">The message.</param>
    public void OnData(PeerId peer, ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            if (!_peers.TryGetValue(peer, out var known) || !known.DataOpen || !known.Connected)
            {
                return;
            }
        }

        DataReceived?.Invoke(this, peer, data);
    }

    /// <summary>
    /// Takes the statistics of a connection and decides which camera stream the participant gets.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="traffic">The statistics.</param>
    public void OnStats(PeerId peer, PeerTraffic traffic)
    {
        VideoQuality? changed = null;
        lock (_gate)
        {
            if (!_peers.TryGetValue(peer, out var known))
            {
                return;
            }

            known.Traffic = traffic;
            if (known.Connected && known.Quality.Update(Finite(traffic.FractionLost), Finite(traffic.RoundTrip)))
            {
                changed = known.Quality.Quality;
            }
        }

        if (changed is { } quality)
        {
            _rtc.SetEconomy(peer, quality == VideoQuality.Low);
            _logger.LogInformation(
                "Camera stream for {Peer}: {Quality} (loss {Loss:P0}, round trip {RoundTrip:0.000} s)",
                peer.ToShortString(), quality, traffic.FractionLost ?? 0, traffic.RoundTrip ?? 0);
            VideoQualityChanged?.Invoke(this, new VideoQualityChange(peer, quality));
        }
    }

    /// <summary>
    /// Takes the level of a voice, measured every <see cref="LevelPeriod"/>.
    /// </summary>
    /// <param name="peer">The participant, or <see langword="default"/> for this device.</param>
    /// <param name="level">The level as the browser gives it: 0 is silence, 1 is full scale.</param>
    public void OnLevel(PeerId peer, double level)
    {
        lock (_gate)
        {
            if (!_started || (peer.IsEmpty ? _microphoneMuted : !_peers.ContainsKey(peer)))
            {
                return;
            }
        }

        var db = level > 0 && double.IsFinite(level) ? 20 * Math.Log10(Math.Min(level, 1)) : SILENCE_DB;
        Raise(_voices.ProcessLevel(peer, db, LevelPeriod, _time.GetElapsedTime(_start)));
    }

    /// <summary>
    /// Takes the end of a device in the middle of the meeting: it was unplugged, or the system took it.
    /// </summary>
    /// <param name="kind">The device.</param>
    public void OnDeviceEnded(CaptureDeviceKind kind)
    {
        lock (_gate)
        {
            if (kind == CaptureDeviceKind.Camera)
            {
                if (_cameraOff)
                {
                    return;
                }

                _cameraOff = true;
            }
            else
            {
                if (_microphoneMuted)
                {
                    return;
                }

                _microphoneMuted = true;
            }
        }

        _logger.LogWarning("The {Device} stopped", kind);
        MuteChanged?.Invoke(this, EventArgs.Empty);
        (kind == CaptureDeviceKind.Camera ? CameraFailed : MicrophoneFailed)?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Turns a STUN address of the sessions (<c>stun://host:port</c>) into the form of the browser.
    /// </summary>
    /// <param name="stun">The address.</param>
    /// <returns>The address as <c>stun:host:port</c>.</returns>
    /// <exception cref="ArgumentException">The address is not a STUN address.</exception>
    private static string ToBrowserStun(string stun)
    {
        var address = stun.StartsWith("stun://", StringComparison.Ordinal) ? stun["stun://".Length..]
            : stun.StartsWith("stun:", StringComparison.Ordinal) ? stun["stun:".Length..]
            : throw new ArgumentException("A STUN address must start with stun:.", nameof(stun));
        if (!PeerEndpoint.TryParse(address, out _))
        {
            throw new ArgumentException("A STUN address must name a host and a port.", nameof(stun));
        }

        return "stun:" + address;
    }

    /// <summary>
    /// Turns a statistic the browser could not give (not a number) into an unknown one.
    /// </summary>
    /// <param name="value">The statistic.</param>
    /// <returns>The statistic, or <see langword="null"/>.</returns>
    private static double? Finite(double? value) => value is { } v && double.IsFinite(v) ? v : null;

    /// <summary>
    /// Finds the connection a signal is for, making the answering side of a new one, and checks that the signal fits
    /// its role.
    /// </summary>
    /// <param name="signal">The checked signal.</param>
    /// <returns>The connection, or <see langword="null"/> when the signal is dropped.</returns>
    private BrowserPeer? Accept(ConferenceSignal signal)
    {
        BrowserPeer? peer;
        BrowserPeer? stale = null;
        lock (_gate)
        {
            _peers.TryGetValue(signal.PeerId, out peer);
            if (signal.Kind == SignalKinds.OFFER)
            {
                var session = SignalRules.ParseSession(signal.Payload);
                if (peer is { IsOfferer: true })
                {
                    peer = null;
                }
                else if (peer is null || (peer.RemoteSession is { } known && session is not null && session != known))
                {
                    // A first offer, or the other side gave up a connection that failed and made a new one: the new
                    // one is answered from scratch.
                    stale = peer;
                    peer = new BrowserPeer(signal.PeerId, isOfferer: false, attempt: 0) { RemoteSession = session };
                }
                else
                {
                    peer.RemoteSession ??= session;
                }
            }
            else if (signal.Kind == SignalKinds.ANSWER && peer is { IsOfferer: false })
            {
                peer = null;
            }
        }

        if (peer is null)
        {
            _logger.LogWarning("A {Kind} from {Peer} does not fit the connection and is dropped", signal.Kind, signal.PeerId.ToShortString());
            return null;
        }

        if (stale is not null)
        {
            _logger.LogInformation("{Peer} made a new media connection", signal.PeerId.ToShortString());
            Replace(stale, peer);
        }
        else if (signal.Kind == SignalKinds.OFFER && !IsCurrent(peer))
        {
            Open(peer);
        }

        return peer;
    }

    /// <summary>
    /// Puts a new connection into the table and opens it in the browser.
    /// </summary>
    /// <param name="peer">The connection.</param>
    private void Open(BrowserPeer peer)
    {
        BrowserIceServer[] servers;
        double? volume;
        lock (_gate)
        {
            _peers[peer.Id] = peer;
            servers = _relay is { } relay ? [new BrowserIceServer(_stun), relay] : [new BrowserIceServer(_stun)];
            volume = _volumes.TryGetValue(peer.Id, out var set) ? set : null;
            peer.ConnectTimer = _time.CreateTimer(_ => OnConnectTimeout(peer), null, ConnectionRules.ConnectTimeout, Timeout.InfiniteTimeSpan);
        }

        _logger.LogInformation("Connecting the cameras with {Peer} ({Role}, attempt {Attempt})", peer.Id.ToShortString(), peer.IsOfferer ? "offer" : "answer", peer.Attempt + 1);
        _rtc.Open(peer.Id, peer.IsOfferer, servers);
        if (volume is { } gain)
        {
            _rtc.SetVolume(peer.Id, gain);
        }
    }

    /// <summary>
    /// Closes a connection and opens another one in its place.
    /// </summary>
    /// <param name="stale">The connection given up.</param>
    /// <param name="fresh">The new one.</param>
    private void Replace(BrowserPeer stale, BrowserPeer fresh)
    {
        lock (_gate)
        {
            if (!_peers.TryGetValue(stale.Id, out var current) || current != stale)
            {
                return;
            }

            _peers.Remove(stale.Id);
        }

        stale.Dispose();
        _rtc.Close(stale.Id);
        Open(fresh);
    }

    /// <summary>
    /// Tells whether a connection is still the one kept for its participant.
    /// </summary>
    /// <param name="peer">The connection.</param>
    /// <returns><see langword="true"/> when it was not replaced or removed.</returns>
    private bool IsCurrent(BrowserPeer peer)
    {
        lock (_gate)
        {
            return _peers.TryGetValue(peer.Id, out var current) && current == peer;
        }
    }

    /// <summary>
    /// Fails a connection that was not made in time.
    /// </summary>
    /// <param name="peer">The connection.</param>
    private void OnConnectTimeout(BrowserPeer peer)
    {
        lock (_gate)
        {
            if (peer.Connected)
            {
                return;
            }
        }

        Fail(peer, "not connected in time");
    }

    /// <summary>
    /// Handles a failed connection as the applications do: the answering side waits for a new offer, the offering
    /// side makes the connection again after a pause that grows with every failure in a row.
    /// </summary>
    /// <param name="peer">The connection.</param>
    /// <param name="reason">Why it failed, for the log.</param>
    private void Fail(BrowserPeer peer, string reason)
    {
        lock (_gate)
        {
            if (peer.Failing || !_peers.TryGetValue(peer.Id, out var current) || current != peer)
            {
                return;
            }

            peer.Failing = true;
            peer.DataOpen = false;
        }

        if (!peer.IsOfferer)
        {
            _logger.LogWarning("The media connection of {Peer} failed ({Reason}); waiting for a new offer", peer.Id.ToShortString(), reason);
            return;
        }

        var delay = ConnectionRules.RestartDelay(peer.Attempt);
        _logger.LogWarning("The media connection of {Peer} failed ({Reason}); connecting again in {Delay:0} s", peer.Id.ToShortString(), reason, delay.TotalSeconds);
        lock (_gate)
        {
            peer.RestartTimer = _time.CreateTimer(_ => Restart(peer), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Makes a failed connection again, unless it found its way back by itself in the meantime.
    /// </summary>
    /// <param name="peer">The failed connection.</param>
    private void Restart(BrowserPeer peer)
    {
        lock (_gate)
        {
            if (peer.Connected)
            {
                peer.Failing = false;
                _logger.LogInformation("The media connection of {Peer} recovered", peer.Id.ToShortString());
                return;
            }
        }

        Replace(peer, new BrowserPeer(peer.Id, isOfferer: true, attempt: peer.WasConnected ? 0 : peer.Attempt + 1));
    }

    /// <summary>
    /// Records the opening or closing of the data channel of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="open">Whether the channel is open.</param>
    private void SetDataOpen(PeerId peer, bool open)
    {
        lock (_gate)
        {
            if (_peers.TryGetValue(peer, out var known))
            {
                known.DataOpen = open;
            }
        }
    }

    /// <summary>
    /// Asks the browser for the devices of the current state and takes back what it could not give.
    /// </summary>
    /// <returns>A task that completes when the devices are set.</returns>
    private async Task ApplyDevicesAsync()
    {
        await _devices.WaitAsync().ConfigureAwait(false);
        try
        {
            bool camera;
            bool microphone;
            string? cameraId;
            string? microphoneId;
            lock (_gate)
            {
                if (!_started)
                {
                    return;
                }

                camera = !_cameraOff;
                microphone = !_microphoneMuted;
                cameraId = _cameraId;
                microphoneId = _microphoneId;
            }

            var result = await _rtc.SetDevicesAsync(camera, microphone, cameraId, microphoneId).ConfigureAwait(false);

            var cameraLost = camera && result.Camera != CaptureOutcome.Started;
            var microphoneLost = microphone && result.Microphone != CaptureOutcome.Started;
            if (!cameraLost && !microphoneLost)
            {
                return;
            }

            lock (_gate)
            {
                _cameraOff |= cameraLost;
                _microphoneMuted |= microphoneLost;
            }

            MuteChanged?.Invoke(this, EventArgs.Empty);
            if (cameraLost)
            {
                _logger.LogWarning("The camera did not start: {Outcome}", result.Camera);
                CameraFailed?.Invoke(this, EventArgs.Empty);
            }

            if (microphoneLost)
            {
                _logger.LogWarning("The microphone did not start: {Outcome}", result.Microphone);
                MicrophoneFailed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _devices.Release();
        }
    }

    /// <summary>
    /// Sets the devices without letting a failure of the browser escape.
    /// </summary>
    /// <returns>A task that completes when the devices are set.</returns>
    private async Task ApplyDevicesSafelyAsync()
    {
        try
        {
            await ApplyDevicesAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning("The devices were not changed: {Reason}", ex.Message);
        }
    }

    /// <summary>
    /// Ends the speaking state of participants who went quiet.
    /// </summary>
    private void ExpireVoices()
    {
        foreach (var change in _voices.Expire(_time.GetElapsedTime(_start)))
        {
            Raise(change);
        }
    }

    /// <summary>
    /// Raises <see cref="SpeakingChanged"/> for a change, if there is one.
    /// </summary>
    /// <param name="change">The change, or <see langword="null"/>.</param>
    private void Raise(SpeakingChange? change)
    {
        if (change is { } value)
        {
            SpeakingChanged?.Invoke(this, value);
        }
    }
}
