using Golether.Browser.Media;
using Golether.Browser.Transport;
using Golether.Client.Services;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Core.Time;
using Golether.Localization;
using Golether.Media.Conference;
using Golether.Media.Conference.Browser;
using Golether.Security.Admission;
using Golether.Security.Invites;
using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.Browser.Session;

/// <summary>
/// The session of the page: the same <see cref="HostSession"/> or <see cref="ParticipantSession"/> as on the
/// desktop, put together with the pieces of the browser (WebRTC, the cameras, the film element, the chosen file).
/// </summary>
internal sealed class BrowserSession : ISessionMessages, IAsyncDisposable
{
    /// <summary>
    /// How long an invitation of the page lets guests in.
    /// </summary>
    private static readonly TimeSpan _inviteLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The settings of a session in a browser: the cache of a participant stays in memory, so it is smaller than on
    /// the desktop, and the relay of a host is reached directly, a page cannot open a local entrance to it.
    /// </summary>
    private static readonly SessionOptions _options = new() { MediaCacheCapacity = 256L * 1024 * 1024, OpensRelayTunnel = false };

    /// <summary>
    /// The session hosted on this page, or <see langword="null"/>.
    /// </summary>
    private readonly HostSession? _host;

    /// <summary>
    /// The session joined from this page, or <see langword="null"/>.
    /// </summary>
    private readonly ParticipantSession? _participant;

    /// <summary>
    /// The cameras, the voices and the data channels of the page.
    /// </summary>
    private readonly BrowserConferenceMedia _conference;

    /// <summary>
    /// The participants speaking now; <see langword="default"/> stands for this device.
    /// </summary>
    private readonly HashSet<PeerId> _speaking = [];

    /// <summary>
    /// Guards <see cref="_speaking"/>: the levels come from the conference.
    /// </summary>
    private readonly Lock _speakingGate = new();

    /// <summary>
    /// The invitation handed out, as the part of the link after the prefix.
    /// </summary>
    private string? _invite;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserSession"/> class.
    /// </summary>
    /// <param name="host">The hosted session, or <see langword="null"/>.</param>
    /// <param name="participant">The joined session, or <see langword="null"/>.</param>
    /// <param name="conference">The cameras of the page.</param>
    private BrowserSession(HostSession? host, ParticipantSession? participant, BrowserConferenceMedia conference)
    {
        _host = host;
        _participant = participant;
        _conference = conference;
        _conference.SpeakingChanged += OnSpeakingChanged;
        _conference.MuteChanged += OnMuteChanged;
        _conference.CameraFailed += OnCameraFailed;
        _conference.MicrophoneFailed += OnMicrophoneFailed;

        if (host is not null)
        {
            host.ChatReceived += OnChat;
            host.DrawReceived += OnStroke;
            host.AvatarReceived += OnAvatar;
            host.Changed += OnChanged;
            host.EventRaised += OnEvent;
        }

        if (participant is not null)
        {
            participant.ChatReceived += OnChat;
            participant.DrawReceived += OnStroke;
            participant.AvatarReceived += OnAvatar;
            participant.Changed += OnChanged;
            participant.EventRaised += OnEvent;
        }
    }

    /// <summary>
    /// Raised when anything of the session changed; the snapshot tells what. May be raised on any thread.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised with a line for the event feed. May be raised on any thread.
    /// </summary>
    public event EventHandler<SessionEvent>? EventRaised;

    /// <inheritdoc />
    public event EventHandler<ChatEntry>? ChatReceived;

    /// <inheritdoc />
    public event EventHandler<StrokeUpdate>? DrawReceived;

    /// <summary>
    /// Raised when a participant showed or reset its avatar. May be raised on any thread.
    /// </summary>
    public event EventHandler<AvatarUpdate>? AvatarReceived;

    /// <summary>
    /// Gets a value indicating whether this page hosts the session.
    /// </summary>
    public bool IsHost => _host is not null;

    /// <summary>
    /// Gets a value indicating whether the microphone of this page is off.
    /// </summary>
    public bool MicrophoneMuted => _conference.MicrophoneMuted;

    /// <summary>
    /// Gets a value indicating whether the camera of this page is off.
    /// </summary>
    public bool CameraOff => _conference.CameraOff;

    /// <summary>
    /// Creates a session on this page: takes a room on the rendezvous server and starts accepting guests.
    /// </summary>
    /// <param name="app">The page.</param>
    /// <param name="sessionName">The session name.</param>
    /// <param name="displayName">The name of the host.</param>
    /// <returns>The session; the film is shared with <see cref="ShareAsync"/>.</returns>
    /// <exception cref="IOException">The room was not taken, or the page has no signalling to take it at.</exception>
    public static async Task<BrowserSession> HostAsync(BrowserApp app, string sessionName, string displayName)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.Config.Signal is not { } signal)
        {
            throw new IOException(Texts.Get("Browser.Host.NoServer"));
        }

        var point = new RendezvousPoint(app.Config.Meet, RendezvousPoint.CreateRoom());
        var listener = await WebRtcListener.OpenAsync(signal, app.Config.StunUrl, point.Room).ConfigureAwait(false);
        var host = new HostSession(
            app.LocalPeer,
            listener,
            new InviteRegistry(TimeProvider.System),
            new AdmissionService(app.Prompt, new AdmissionOptions(), TimeProvider.System),
            null,
            app.Player,
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            sessionName,
            displayName,
            _options,
            app.LoggerFactory,
            app.Conference)
        {
            Rendezvous = point,
            Signal = signal,
        };

        var session = new BrowserSession(host, null, app.Conference);
        host.Start();
        return session;
    }

    /// <summary>
    /// Creates the session of a guest; it joins with <see cref="JoinAsync"/>, and until the host lets it in its
    /// snapshot shows the verification code.
    /// </summary>
    /// <param name="app">The page.</param>
    /// <param name="displayName">The name of the guest.</param>
    /// <returns>The session.</returns>
    public static BrowserSession CreateGuest(BrowserApp app, string displayName)
    {
        ArgumentNullException.ThrowIfNull(app);

        var connector = new WebRtcConnector(app.Config);
        var participant = new ParticipantSession(
            app.LocalPeer,
            connector,
            app.Player,
            app.Streams,
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            displayName,
            _options,
            app.LoggerFactory,
            app.Conference,
            null,
            connector);
        return new BrowserSession(null, participant, app.Conference);
    }

    /// <summary>
    /// Shares a chosen file with everybody.
    /// </summary>
    /// <param name="path">The path of the file (<see cref="BrowserFiles.Add"/>).</param>
    /// <returns>A task that completes when the film is announced.</returns>
    public async Task ShareAsync(string path)
        => await Host.ShareMediaAsync(path, CancellationToken.None).ConfigureAwait(false);

    /// <summary>
    /// Joins the session of an invitation; completes when the host let this page in.
    /// </summary>
    /// <param name="invite">The invitation.</param>
    /// <param name="cancellationToken">Cancels the wait for the host.</param>
    /// <returns>A task that fails with the reason when the session was not joined.</returns>
    public Task JoinAsync(Invite invite, CancellationToken cancellationToken)
        => (_participant ?? throw new InvalidOperationException("This page hosts the session.")).JoinAsync(invite, cancellationToken);

    /// <summary>
    /// Gives the invitation of the session, making it on the first call.
    /// </summary>
    /// <param name="renew">Whether to make a new invitation instead of the one handed out.</param>
    /// <returns>The part of the link after the prefix; the page puts its own address before it.</returns>
    public string Invite(bool renew)
    {
        if (renew || _invite is null)
        {
            _invite = Host.CreateInvite([], _inviteLifetime).ToLink()[Security.Invites.Invite.LINK_PREFIX.Length..];
        }

        return _invite;
    }

    /// <summary>
    /// Takes a snapshot of the session.
    /// </summary>
    /// <returns>The snapshot.</returns>
    public SessionSnapshot GetSnapshot() => _host?.GetSnapshot() ?? _participant!.GetSnapshot();

    /// <summary>
    /// Tells whether a participant speaks now.
    /// </summary>
    /// <param name="peer">The participant, or <see langword="default"/> for this device.</param>
    /// <returns><see langword="true"/> while the voice is heard.</returns>
    public bool IsSpeaking(PeerId peer)
    {
        lock (_speakingGate)
        {
            return _speaking.Contains(peer);
        }
    }

    /// <summary>
    /// Switches the microphone and the camera of this page.
    /// </summary>
    /// <param name="microphone">Whether the microphone is on.</param>
    /// <param name="camera">Whether the camera is on.</param>
    public void SetDevices(bool microphone, bool camera) => _conference.SetMuted(!microphone, !camera);

    /// <summary>
    /// Sets how loud a participant is heard on this page.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="percent">The volume, 0–100.</param>
    public void SetVoiceVolume(PeerId peer, double percent) => _conference.SetVoiceVolume(peer, Math.Clamp(percent, 0, 100) / 100);

    /// <inheritdoc />
    public Task SendChatAsync(ChatKind kind, string text, CancellationToken cancellationToken)
        => _host?.SendChatAsync(kind, text) ?? _participant!.SendChatAsync(kind, text, cancellationToken);

    /// <inheritdoc />
    public Task SendDrawAsync(string strokeId, StrokePhase phase, IReadOnlyList<StrokePoint> points, CancellationToken cancellationToken)
        => _host?.SendDrawAsync(strokeId, phase, points) ?? _participant!.SendDrawAsync(strokeId, phase, points, cancellationToken);

    /// <summary>
    /// Switches devices of a guest off (host).
    /// </summary>
    /// <param name="peer">The guest.</param>
    /// <param name="microphone">Whether to switch the microphone off.</param>
    /// <param name="camera">Whether to switch the camera off.</param>
    /// <returns>A task that completes when the guest was told.</returns>
    public Task SwitchOffAsync(PeerId peer, bool microphone, bool camera) => Host.SwitchOffParticipantDevicesAsync(peer, microphone, camera);

    /// <summary>
    /// Removes a guest from the session (host).
    /// </summary>
    /// <param name="peer">The guest.</param>
    /// <returns>A task that completes when the guest was removed.</returns>
    public Task RemoveAsync(PeerId peer) => Host.RemoveParticipantAsync(peer);

    /// <summary>
    /// Sends an intention about the playback: the host decides, a guest asks the host.
    /// </summary>
    /// <param name="request">The intention.</param>
    /// <returns>A task that completes when the intention was sent.</returns>
    public Task RequestAsync(PlaybackRequest request)
        => _host?.RequestAsync(request, CancellationToken.None) ?? _participant!.RequestAsync(request, CancellationToken.None);

    /// <summary>
    /// Leaves the session and stops the cameras.
    /// </summary>
    /// <returns>A task that completes when the session is over.</returns>
    public async ValueTask DisposeAsync()
    {
        _conference.SpeakingChanged -= OnSpeakingChanged;
        _conference.MuteChanged -= OnMuteChanged;
        _conference.CameraFailed -= OnCameraFailed;
        _conference.MicrophoneFailed -= OnMicrophoneFailed;

        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
        }

        if (_participant is not null)
        {
            await _participant.DisposeAsync().ConfigureAwait(false);
        }

        BrowserFiles.Clear();
    }

    /// <summary>
    /// Remembers who speaks.
    /// </summary>
    /// <param name="sender">The conference.</param>
    /// <param name="change">The change.</param>
    private void OnSpeakingChanged(object? sender, SpeakingChange change)
    {
        lock (_speakingGate)
        {
            if (change.IsSpeaking)
            {
                _speaking.Add(change.PeerId);
            }
            else
            {
                _speaking.Remove(change.PeerId);
            }
        }

        OnChanged(this, EventArgs.Empty);
    }

    /// <summary>
    /// Passes on a change of the session.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="e">Nothing.</param>
    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Passes on a line for the event feed.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="e">The line.</param>
    private void OnEvent(object? sender, SessionEvent e) => EventRaised?.Invoke(this, e);

    /// <summary>
    /// Passes on a chat message or a reaction.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="entry">The message.</param>
    private void OnChat(object? sender, ChatEntry entry) => ChatReceived?.Invoke(this, entry);

    /// <summary>
    /// Passes on a part of a pen stroke.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="update">The part.</param>
    private void OnStroke(object? sender, StrokeUpdate update) => DrawReceived?.Invoke(this, update);

    /// <summary>
    /// Passes on the avatar of a participant.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="update">The avatar.</param>
    private void OnAvatar(object? sender, AvatarUpdate update) => AvatarReceived?.Invoke(this, update);

    /// <summary>
    /// Shows a switched device.
    /// </summary>
    /// <param name="sender">The conference.</param>
    /// <param name="e">Nothing.</param>
    private void OnMuteChanged(object? sender, EventArgs e) => OnChanged(this, EventArgs.Empty);

    /// <summary>
    /// Tells that the camera does not work.
    /// </summary>
    /// <param name="sender">The conference.</param>
    /// <param name="e">Nothing.</param>
    private void OnCameraFailed(object? sender, EventArgs e)
        => EventRaised?.Invoke(this, new SessionEvent(TimeProvider.System.GetLocalNow(), null, Texts.Get("Browser.Camera.Failed")));

    /// <summary>
    /// Tells that the microphone does not work.
    /// </summary>
    /// <param name="sender">The conference.</param>
    /// <param name="e">Nothing.</param>
    private void OnMicrophoneFailed(object? sender, EventArgs e)
        => EventRaised?.Invoke(this, new SessionEvent(TimeProvider.System.GetLocalNow(), null, Texts.Get("Browser.Microphone.Failed")));

    /// <summary>
    /// Gets the hosted session.
    /// </summary>
    private HostSession Host => _host ?? throw new InvalidOperationException("This page is a guest of the session.");
}
