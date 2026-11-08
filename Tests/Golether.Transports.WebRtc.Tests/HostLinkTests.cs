using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Golether.Core.Data.Enums;

namespace Golether.Transports.WebRtc.Tests;

/// <summary>
/// Tests of the connection of a host with one WebRTC guest: the guest proves its key before any stream reaches the
/// session, and connections that are not proven or stay idle are closed.
/// </summary>
public sealed class HostLinkTests : IDisposable
{
    /// <summary>
    /// The room.
    /// </summary>
    private const string ROOM = "AAAAAAAAAAAAAAAAAAAAAA";

    /// <summary>
    /// The description of the host.
    /// </summary>
    private const string HOST_SDP = "v=0\r\na=fingerprint:sha-256 AA:BB\r\nm=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\n";

    /// <summary>
    /// The description of the guest.
    /// </summary>
    private const string GUEST_SDP = "v=0\r\na=fingerprint:sha-256 CC:DD\r\nm=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\n";

    /// <summary>
    /// The key of the host.
    /// </summary>
    private readonly ECDsa _hostKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>
    /// The key of the guest.
    /// </summary>
    private readonly ECDsa _guestKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>
    /// The source of time.
    /// </summary>
    private readonly FakeTimeProvider _time = new();

    /// <summary>
    /// The connection.
    /// </summary>
    private readonly FakeDataLink _link = new() { LocalDescription = HOST_SDP, RemoteDescription = GUEST_SDP };

    /// <summary>
    /// The streams the session got.
    /// </summary>
    private readonly List<PeerStream> _offered = [];

    /// <summary>
    /// The data passed to the guest through the signalling.
    /// </summary>
    private readonly List<string> _sent = [];

    /// <inheritdoc />
    public void Dispose()
    {
        _hostKey.Dispose();
        _guestKey.Dispose();
    }

    /// <summary>
    /// A guest that proves its key gets the proof of the host back, and its channels reach the session as streams of
    /// its identifier and their purpose.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task ProvenGuest_GetsStreams()
    {
        await using var link = Create();
        var (auth, guestAuth) = FakeDataChannel.Pair(PeerProof.AUTH_LABEL);
        var reply = new TaskCompletionSource<string>();
        guestAuth.MessageReceived += (_, message) => reply.TrySetResult(message.Text!);
        var text = PeerProof.Text(HOST_SDP, GUEST_SDP, ROOM);

        _link.Open(auth);
        guestAuth.SendText(PeerProof.Create(Signer(_guestKey), text));

        Assert.True(PeerProof.TryVerify(await reply.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), text, out var host));
        Assert.Equal(Signer(_hostKey).PeerId, host);

        _link.Open(FakeDataChannel.Pair("2").A);
        await WaitAsync(() => _offered.Count == 1);
        Assert.Equal(Signer(_guestKey).PeerId, _offered[0].RemotePeer);
        Assert.Equal(StreamPurpose.MediaData, _offered[0].Purpose);
        Assert.True(link.IsProven);
    }

    /// <summary>
    /// A proof over another room (or of another connection) closes the connection; no stream reaches the session.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WrongProof_ClosesTheConnection()
    {
        await using var link = Create();
        var ended = new TaskCompletionSource();
        link.Ended += (_, _) => ended.TrySetResult();
        var (auth, guestAuth) = FakeDataChannel.Pair(PeerProof.AUTH_LABEL);

        _link.Open(auth);
        _link.Open(FakeDataChannel.Pair("1").A);
        guestAuth.SendText(PeerProof.Create(Signer(_guestKey), PeerProof.Text(HOST_SDP, GUEST_SDP, "another room")));

        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(_link.Disposed);
        Assert.Empty(_offered);
    }

    /// <summary>
    /// A connection that is not proven in time, and a proven one left without streams, are closed.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task UnprovenAndIdleConnections_AreClosed()
    {
        await using var silent = Create();
        _time.Advance(new WebRtcOptions().ConnectTimeout + new WebRtcOptions().ProofTimeout);
        await WaitAsync(() => _link.Disposed);

        var idleLink = new FakeDataLink { LocalDescription = HOST_SDP, RemoteDescription = GUEST_SDP };
        await using var idle = new WebRtcHostLink(idleLink, ROOM, Signer(_hostKey), stream =>
        {
            _offered.Add(stream);
            return true;
        }, data => Task.CompletedTask, new WebRtcOptions(), _time, NullLogger.Instance);
        var (auth, guestAuth) = FakeDataChannel.Pair(PeerProof.AUTH_LABEL);
        idleLink.Open(auth);
        guestAuth.SendText(PeerProof.Create(Signer(_guestKey), PeerProof.Text(HOST_SDP, GUEST_SDP, ROOM)));
        await WaitAsync(() => idle.IsProven);

        _time.Advance(new WebRtcOptions().IdleTimeout);
        await WaitAsync(() => idleLink.Disposed);
    }

    /// <summary>
    /// A channel whose label is no purpose is refused, and so is any channel past the limit.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task ForeignAndExtraChannels_AreRefused()
    {
        await using var link = Create(new WebRtcOptions { MaxChannelsPerPeer = 2 });
        var (auth, guestAuth) = FakeDataChannel.Pair(PeerProof.AUTH_LABEL);
        _link.Open(auth);
        guestAuth.SendText(PeerProof.Create(Signer(_guestKey), PeerProof.Text(HOST_SDP, GUEST_SDP, ROOM)));
        await WaitAsync(() => link.IsProven);

        var foreign = FakeDataChannel.Pair("x").A;
        var extra = FakeDataChannel.Pair("1").A;
        _link.Open(foreign);
        _link.Open(extra);

        await WaitAsync(() => !foreign.IsOpen && !extra.IsOpen);
        Assert.Empty(_offered);
    }

    /// <summary>
    /// The offer of the guest is answered once; candidates go to the connection; malformed data is skipped.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task Signals_AreAnsweredOnce()
    {
        await using var link = Create();

        await link.OnSignalAsync(SignalEnvelope.ForOffer(GUEST_SDP));
        await link.OnSignalAsync(SignalEnvelope.ForOffer(GUEST_SDP));
        await link.OnSignalAsync(SignalEnvelope.ForCandidate(new IceCandidate(0, "candidate:1 1 udp 1 192.0.2.1 5000 typ host")));
        await link.OnSignalAsync("{\"offer\":{\"type\":\"offer\",\"sdp\":\"not sdp\"}}");
        await link.OnSignalAsync("garbage");

        Assert.Single(_link.Offers);
        Assert.Single(_sent);
        Assert.True(SignalEnvelope.TryRead(_sent[0], out var answer) && answer.Answer?.Sdp == HOST_SDP);
        Assert.Equal(new IceCandidate(0, "candidate:1 1 udp 1 192.0.2.1 5000 typ host"), Assert.Single(_link.Candidates));
    }

    /// <summary>
    /// Makes the signer of a key as WebCrypto signs.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The signer.</returns>
    private static DeviceSigner Signer(ECDsa key)
        => new(key.ExportSubjectPublicKeyInfo(), data => key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    /// <summary>
    /// Waits for a condition that work started in the background brings about.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    /// <summary>
    /// Makes the connection under test.
    /// </summary>
    /// <param name="options">The limits, or <see langword="null"/> for the defaults.</param>
    /// <returns>The connection.</returns>
    private WebRtcHostLink Create(WebRtcOptions? options = null)
        => new(_link, ROOM, Signer(_hostKey), stream =>
        {
            _offered.Add(stream);
            return true;
        }, data =>
        {
            _sent.Add(data);
            return Task.CompletedTask;
        }, options ?? new WebRtcOptions(), _time, NullLogger.Instance);
}
