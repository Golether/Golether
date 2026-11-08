using Microsoft.Extensions.Time.Testing;
using Golether.Core.Identity;
using Golether.Media.Conference.Browser.Enums;

namespace Golether.Media.Conference.Browser.Tests;

/// <summary>
/// Tests of how a browser connects its cameras with a participant: who offers, which signals are taken, and how a
/// failed connection is made again, by the same rules as the applications.
/// </summary>
public sealed class BrowserSignalingTests : IAsyncDisposable
{
    /// <summary>
    /// A description with an origin line of session 1.
    /// </summary>
    private const string OFFER_1 = "v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\ns=-\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\n";

    /// <summary>
    /// A description with an origin line of session 2: the other side made a new connection.
    /// </summary>
    private const string OFFER_2 = "v=0\r\no=- 2 2 IN IP4 127.0.0.1\r\ns=-\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\n";

    /// <summary>
    /// A participant.
    /// </summary>
    private static readonly PeerId _bob = PeerId.Parse(new string('b', 64));

    /// <summary>
    /// The source of time.
    /// </summary>
    private readonly FakeTimeProvider _time = new();

    /// <summary>
    /// The browser.
    /// </summary>
    private readonly FakeBrowserRtc _rtc = new();

    /// <summary>
    /// The cameras under test.
    /// </summary>
    private readonly BrowserConferenceMedia _media;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserSignalingTests"/> class.
    /// </summary>
    public BrowserSignalingTests()
    {
        _media = new BrowserConferenceMedia(_rtc, "stun:203.0.113.7:47900", timeProvider: _time);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _media.DisposeAsync();

    /// <summary>
    /// A connection is opened once per participant, with the STUN server of the session in the form of the browser;
    /// without one the page's own server is used, and an address that is not STUN is refused.
    /// </summary>
    [Fact]
    public async Task Connect_OpensOncePerParticipant()
    {
        _media.SetStunServer("stun://198.51.100.1:3478");
        await _media.ConnectAsync(_bob, isOfferer: true, CancellationToken.None);
        await _media.ConnectAsync(_bob, isOfferer: true, CancellationToken.None);

        var opened = Assert.Single(_rtc.Opened);
        Assert.Equal(_bob, opened.Peer);
        Assert.True(opened.IsOfferer);
        Assert.Equal([new BrowserIceServer("stun:198.51.100.1:3478")], opened.IceServers);

        _media.SetStunServer(null);
        await _media.ConnectAsync(PeerId.Parse(new string('c', 64)), isOfferer: false, CancellationToken.None);
        Assert.Equal([new BrowserIceServer("stun:203.0.113.7:47900")], _rtc.Opened[^1].IceServers);

        _media.SetRelay("turn://user%201:p%40ss@198.51.100.1:47801?transport=tcp");
        await _media.ConnectAsync(PeerId.Parse(new string('d', 64)), isOfferer: false, CancellationToken.None);
        Assert.Equal(new BrowserIceServer("turn:198.51.100.1:47801?transport=tcp", "user 1", "p@ss"), _rtc.Opened[^1].IceServers[^1]);
        Assert.Throws<ArgumentException>(() => _media.SetRelay("turn://198.51.100.1:47801"));

        Assert.Throws<ArgumentException>(() => _media.SetStunServer("turn://198.51.100.1:3478"));
        Assert.Throws<ArgumentException>(() => _media.SetStunServer("stun://nowhere"));
    }

    /// <summary>
    /// Signals of a wrong shape are refused before the browser sees them: an unknown kind, an oversized payload, a
    /// description that is not SDP and a malformed candidate.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="payload">The payload.</param>
    [Theory]
    [InlineData("bye", OFFER_1)]
    [InlineData("offer", "not sdp")]
    [InlineData("candidate", "x\ncandidate:1 1 udp 1 192.0.2.1 5000 typ host")]
    [InlineData("candidate", "0\nnot a candidate")]
    [InlineData("candidate", "0\r\ncandidate:1 1 udp 1 192.0.2.1 5000 typ host")]
    public async Task MalformedSignals_AreRefused(string kind, string payload)
    {
        await Assert.ThrowsAsync<FormatException>(() => _media.HandleSignalAsync(new ConferenceSignal(_bob, kind, payload), CancellationToken.None));

        Assert.Empty(_rtc.Opened);
        Assert.Empty(_rtc.Descriptions);
        Assert.Empty(_rtc.Candidates);
    }

    /// <summary>
    /// A payload over the limit of the applications is refused.
    /// </summary>
    [Fact]
    public async Task OversizedSignal_IsRefused()
    {
        var payload = OFFER_1 + new string('a', SignalRules.MAX_SIGNAL_LENGTH);

        await Assert.ThrowsAsync<FormatException>(() => _media.HandleSignalAsync(new ConferenceSignal(_bob, SignalKinds.OFFER, payload), CancellationToken.None));
        Assert.Empty(_rtc.Opened);
    }

    /// <summary>
    /// Signals that do not fit the role are dropped: an offer to the offering side, an answer to the answering side
    /// and an answer from a participant without a connection.
    /// </summary>
    [Fact]
    public async Task SignalsOfTheWrongRole_AreDropped()
    {
        var carol = PeerId.Parse(new string('c', 64));
        var dave = PeerId.Parse(new string('d', 64));
        await _media.ConnectAsync(_bob, isOfferer: true, CancellationToken.None);
        await _media.ConnectAsync(carol, isOfferer: false, CancellationToken.None);

        await _media.HandleSignalAsync(new ConferenceSignal(_bob, SignalKinds.OFFER, OFFER_1), CancellationToken.None);
        await _media.HandleSignalAsync(new ConferenceSignal(carol, SignalKinds.ANSWER, OFFER_1), CancellationToken.None);
        await _media.HandleSignalAsync(new ConferenceSignal(dave, SignalKinds.ANSWER, OFFER_1), CancellationToken.None);

        Assert.Empty(_rtc.Descriptions);
        Assert.Equal(2, _rtc.Opened.Count);
    }

    /// <summary>
    /// The first offer of a participant opens the answering side and is applied; an answer reaches the offering side;
    /// a candidate is passed on with its m-line.
    /// </summary>
    [Fact]
    public async Task FittingSignals_ReachTheBrowser()
    {
        var carol = PeerId.Parse(new string('c', 64));
        await _media.ConnectAsync(carol, isOfferer: true, CancellationToken.None);

        await _media.HandleSignalAsync(new ConferenceSignal(_bob, SignalKinds.OFFER, OFFER_1), CancellationToken.None);
        await _media.HandleSignalAsync(new ConferenceSignal(carol, SignalKinds.ANSWER, OFFER_1), CancellationToken.None);
        await _media.HandleSignalAsync(new ConferenceSignal(_bob, SignalKinds.CANDIDATE, "1\ncandidate:1 1 udp 1 192.0.2.1 5000 typ host"), CancellationToken.None);

        Assert.Contains(_rtc.Opened, o => o.Peer == _bob && !o.IsOfferer);
        Assert.Equal([(_bob, SignalKinds.OFFER), (carol, SignalKinds.ANSWER)], _rtc.Descriptions);
        Assert.Equal((_bob, 1u, "candidate:1 1 udp 1 192.0.2.1 5000 typ host"), Assert.Single(_rtc.Candidates));
    }

    /// <summary>
    /// The page's own signals reach the session only for participants it is connected with.
    /// </summary>
    [Fact]
    public async Task OwnSignals_GoOnlyToKnownParticipants()
    {
        var signals = new List<ConferenceSignal>();
        _media.SignalReady += (_, signal) => signals.Add(signal);
        await _media.ConnectAsync(_bob, isOfferer: true, CancellationToken.None);

        _media.OnSignal(_bob, SignalKinds.OFFER, OFFER_1);
        _media.OnSignal(PeerId.Parse(new string('c', 64)), SignalKinds.OFFER, OFFER_1);

        Assert.Equal(new ConferenceSignal(_bob, SignalKinds.OFFER, OFFER_1), Assert.Single(signals));
    }

    /// <summary>
    /// An offer with another session identifier means the other side made a new connection: the old one is closed and
    /// a new answering one is opened; an offer of the same session keeps the connection.
    /// </summary>
    [Fact]
    public async Task OfferOfANewSession_ReplacesTheConnection()
    {
        await _media.HandleSignalAsync(new ConferenceSignal(_bob, SignalKinds.OFFER, OFFER_1), CancellationToken.None);
        await _media.HandleSignalAsync(new ConferenceSignal(_bob, SignalKinds.OFFER, OFFER_1), CancellationToken.None);
        Assert.Single(_rtc.Opened);
        Assert.Empty(_rtc.Closed);

        await _media.HandleSignalAsync(new ConferenceSignal(_bob, SignalKinds.OFFER, OFFER_2), CancellationToken.None);

        Assert.Equal([_bob], _rtc.Closed);
        Assert.Equal(2, _rtc.Opened.Count);
        Assert.False(_rtc.Opened[^1].IsOfferer);
        Assert.Equal(3, _rtc.Descriptions.Count);
    }

    /// <summary>
    /// The offering side makes a failed connection again after 3, 6, 12 seconds…; the connection made after one that
    /// was made once starts the count again.
    /// </summary>
    [Fact]
    public async Task FailedConnection_IsMadeAgainAfterGrowingPauses()
    {
        await _media.ConnectAsync(_bob, isOfferer: true, CancellationToken.None);

        _media.OnConnectionState(_bob, PeerLinkState.Failed);
        _time.Advance(TimeSpan.FromSeconds(2.9));
        Assert.Single(_rtc.Opened);
        _time.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Equal(2, _rtc.Opened.Count);
        Assert.True(_rtc.Opened[^1].IsOfferer);

        _media.OnConnectionState(_bob, PeerLinkState.Failed);
        _time.Advance(TimeSpan.FromSeconds(5.9));
        Assert.Equal(2, _rtc.Opened.Count);
        _time.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Equal(3, _rtc.Opened.Count);

        _media.OnConnectionState(_bob, PeerLinkState.Connected);
        _media.OnConnectionState(_bob, PeerLinkState.Failed);
        _time.Advance(TimeSpan.FromSeconds(12));
        Assert.Equal(4, _rtc.Opened.Count);

        _media.OnConnectionState(_bob, PeerLinkState.Failed);
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(5, _rtc.Opened.Count);
        Assert.Equal([_bob, _bob, _bob, _bob], _rtc.Closed);
    }

    /// <summary>
    /// A connection not made within 20 seconds counts as failed.
    /// </summary>
    [Fact]
    public async Task ConnectionNotMadeInTime_IsMadeAgain()
    {
        await _media.ConnectAsync(_bob, isOfferer: true, CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(19));
        Assert.Single(_rtc.Opened);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(_rtc.Opened);
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(2, _rtc.Opened.Count);
    }

    /// <summary>
    /// A connection that found its way back before the pause ended is kept.
    /// </summary>
    [Fact]
    public async Task RecoveredConnection_IsKept()
    {
        await _media.ConnectAsync(_bob, isOfferer: true, CancellationToken.None);

        _media.OnConnectionState(_bob, PeerLinkState.Failed);
        _media.OnConnectionState(_bob, PeerLinkState.Connected);
        _time.Advance(TimeSpan.FromSeconds(30));

        Assert.Single(_rtc.Opened);
        Assert.Empty(_rtc.Closed);
    }

    /// <summary>
    /// The answering side does not make a failed connection again: it waits for the new offer.
    /// </summary>
    [Fact]
    public async Task AnsweringSide_WaitsForANewOffer()
    {
        await _media.ConnectAsync(_bob, isOfferer: false, CancellationToken.None);

        _media.OnConnectionState(_bob, PeerLinkState.Failed);
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Single(_rtc.Opened);
        Assert.Empty(_rtc.Closed);
    }

    /// <summary>
    /// A description the browser refuses fails the connection, and the offering side makes it again.
    /// </summary>
    [Fact]
    public async Task RefusedDescription_FailsTheConnection()
    {
        await _media.ConnectAsync(_bob, isOfferer: true, CancellationToken.None);
        _rtc.DescriptionFailure = new InvalidOperationException("refused");

        await _media.HandleSignalAsync(new ConferenceSignal(_bob, SignalKinds.ANSWER, OFFER_1), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(2, _rtc.Opened.Count);
    }

    /// <summary>
    /// A participant who left is disconnected once, and its timers do nothing afterwards.
    /// </summary>
    [Fact]
    public async Task Disconnect_ClosesTheConnection()
    {
        await _media.ConnectAsync(_bob, isOfferer: true, CancellationToken.None);
        _media.OnConnectionState(_bob, PeerLinkState.Failed);

        await _media.DisconnectAsync(_bob);
        await _media.DisconnectAsync(_bob);
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal([_bob], _rtc.Closed);
        Assert.Single(_rtc.Opened);
    }
}
