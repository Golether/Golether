using Golether.Browser.Interop;
using Golether.Browser.Media;
using Golether.Browser.Transport;
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
/// desktop, put together with the pieces of the browser (WebRTC, the cameras, the <c>&lt;video&gt;</c> element, the
/// chosen file).
/// </summary>
internal sealed class BrowserSession : IAsyncDisposable
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
        }

        if (participant is not null)
        {
            participant.ChatReceived += OnChat;
            participant.DrawReceived += OnStroke;
            participant.AvatarReceived += OnAvatar;
        }

        EventHandler changed = (_, _) => PageInterop.Changed();
        EventHandler<SessionEvent> raised = (_, e) => PageInterop.Event(e.Text);
        if (host is not null)
        {
            host.Changed += changed;
            host.EventRaised += raised;
        }

        if (participant is not null)
        {
            participant.Changed += changed;
            participant.EventRaised += raised;
        }
    }

    /// <summary>
    /// Gets a value indicating whether this page hosts the session.
    /// </summary>
    public bool IsHost => _host is not null;

    /// <summary>
    /// Creates a session on this page: takes a room on the rendezvous server and starts accepting guests.
    /// </summary>
    /// <param name="app">The page.</param>
    /// <param name="sessionName">The session name.</param>
    /// <param name="displayName">The name of the host.</param>
    /// <returns>The session; the film is shared with <see cref="ShareAsync"/>.</returns>
    /// <exception cref="IOException">The room was not taken.</exception>
    public static async Task<BrowserSession> HostAsync(BrowserApp app, string sessionName, string displayName)
    {
        ArgumentNullException.ThrowIfNull(app);

        var point = new RendezvousPoint(app.Config.Meet, RendezvousPoint.CreateRoom());
        var listener = await WebRtcListener.OpenAsync(app.Config, point.Room).ConfigureAwait(false);
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
            Signal = app.Config.Signal,
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
    /// <returns>A task that fails with the reason when the session was not joined.</returns>
    public Task JoinAsync(Invite invite)
        => (_participant ?? throw new InvalidOperationException("This page hosts the session.")).JoinAsync(invite, CancellationToken.None);

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
    /// Reads the state of the session for the page.
    /// </summary>
    /// <returns>The snapshot as JSON.</returns>
    public string Snapshot() => SnapshotJson.Write(_host?.GetSnapshot() ?? _participant!.GetSnapshot(), _conference, _speaking);

    /// <summary>
    /// Switches the microphone and the camera of this page on or off.
    /// </summary>
    /// <param name="microphone">Whether the microphone should be on.</param>
    /// <param name="camera">Whether the camera should be on.</param>
    public void SetDevices(bool microphone, bool camera) => _conference.SetMuted(!microphone, !camera);

    /// <summary>
    /// Sends a chat message or a reaction to everybody.
    /// </summary>
    /// <param name="kind">Text or reaction.</param>
    /// <param name="text">The text, or the reaction.</param>
    /// <returns>A task that completes when the message is sent.</returns>
    public Task SendChatAsync(ChatKind kind, string text)
        => _host?.SendChatAsync(kind, text) ?? _participant!.SendChatAsync(kind, text, CancellationToken.None);

    /// <summary>
    /// Switches the microphone or the camera of a guest off; only the host can.
    /// </summary>
    /// <param name="peer">The guest.</param>
    /// <param name="microphone">Whether to switch the microphone off.</param>
    /// <param name="camera">Whether to switch the camera off.</param>
    /// <returns>A task that completes when the request is sent.</returns>
    public Task SwitchOffAsync(PeerId peer, bool microphone, bool camera) => Host.SwitchOffParticipantDevicesAsync(peer, microphone, camera);

    /// <summary>
    /// Sends an intention of the user to the session.
    /// </summary>
    /// <param name="request">The intention.</param>
    /// <returns>A task that completes when the intention was passed on.</returns>
    public Task RequestAsync(PlaybackRequest request)
        => _host?.RequestAsync(request, CancellationToken.None) ?? _participant!.RequestAsync(request, CancellationToken.None);

    /// <summary>
    /// Leaves the session; a host ends it for everybody.
    /// </summary>
    /// <returns>A task that completes when the session is closed.</returns>
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
    /// Keeps who is speaking and redraws the page.
    /// </summary>
    /// <param name="sender">The cameras.</param>
    /// <param name="change">The change.</param>
    private void OnSpeakingChanged(object? sender, SpeakingChange change)
    {
        if (change.IsSpeaking)
        {
            _speaking.Add(change.PeerId);
        }
        else
        {
            _speaking.Remove(change.PeerId);
        }

        PageInterop.Changed();
    }

    /// <summary>
    /// Shows a chat message or a reaction.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="entry">The message.</param>
    private void OnChat(object? sender, ChatEntry entry) => PageInterop.Chat(PageEventsJson.Chat(entry));

    /// <summary>
    /// Draws a part of a pen stroke.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="update">The part.</param>
    private void OnStroke(object? sender, StrokeUpdate update) => PageInterop.Stroke(PageEventsJson.Stroke(update));

    /// <summary>
    /// Shows the picture a participant chose.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="update">The picture.</param>
    private void OnAvatar(object? sender, AvatarUpdate update)
        => PageInterop.Avatar(update.IsLocal ? string.Empty : update.Peer.Value, update.Image is { Length: > 0 } image ? Convert.ToBase64String(image) : string.Empty);

    /// <summary>
    /// Redraws the buttons of the devices.
    /// </summary>
    /// <param name="sender">The cameras.</param>
    /// <param name="e">Nothing.</param>
    private void OnMuteChanged(object? sender, EventArgs e) => PageInterop.Changed();

    /// <summary>
    /// Tells the user that the camera does not work.
    /// </summary>
    /// <param name="sender">The cameras.</param>
    /// <param name="e">Nothing.</param>
    private void OnCameraFailed(object? sender, EventArgs e) => PageInterop.Event(Texts.Get("Browser.Camera.Failed"));

    /// <summary>
    /// Tells the user that the microphone does not work.
    /// </summary>
    /// <param name="sender">The cameras.</param>
    /// <param name="e">Nothing.</param>
    private void OnMicrophoneFailed(object? sender, EventArgs e) => PageInterop.Event(Texts.Get("Browser.Microphone.Failed"));

    /// <summary>
    /// Gets the hosted session.
    /// </summary>
    private HostSession Host => _host ?? throw new InvalidOperationException("This page is a guest of the session.");
}
