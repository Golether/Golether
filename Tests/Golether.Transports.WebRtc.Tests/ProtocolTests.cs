using System.Security.Cryptography;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;

namespace Golether.Transports.WebRtc.Tests;

/// <summary>
/// Tests of what travels: the proofs as the browser writes them, the envelopes of the signals, the streams over data
/// channels and the way into a session from two sides.
/// </summary>
public sealed class ProtocolTests
{
    /// <summary>
    /// The fingerprints are written as <c>rtc.js</c> writes them: every fingerprint line, upper case, sorted, joined
    /// with commas, the host first.
    /// </summary>
    [Fact]
    public void ProofText_IsWrittenAsTheBrowserWritesIt()
    {
        const string HOST = "v=0\r\na=fingerprint:sha-256 ab:cd\r\na=fingerprint:sha-1 11:22\r\nm=application 9\r\n";
        const string GUEST = "v=0\na=fingerprint:sha-256 0F:0E\nm=application 9\n";

        Assert.Equal("SHA-1 11:22,SHA-256 AB:CD", PeerProof.Fingerprints(HOST));
        Assert.Equal("golether-webrtc-v1\nSHA-1 11:22,SHA-256 AB:CD\nSHA-256 0F:0E\nroom", PeerProof.Text(HOST, GUEST, "room"));
    }

    /// <summary>
    /// A proof holds only for the text it signs; damaged and oversized messages and other curves are refused.
    /// </summary>
    [Fact]
    public void Proof_HoldsOnlyForItsText()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signer = new DeviceSigner(key.ExportSubjectPublicKeyInfo(), data => key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        var proof = PeerProof.Create(signer, "text");

        Assert.True(PeerProof.TryVerify(proof, "text", out var peer));
        Assert.Equal(PeerId.FromSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo()), peer);
        Assert.False(PeerProof.TryVerify(proof, "other text", out _));
        Assert.False(PeerProof.TryVerify("{not json", "text", out _));
        Assert.False(PeerProof.TryVerify(new string('a', PeerProof.MAX_MESSAGE + 1), "text", out _));

        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var otherSigner = new DeviceSigner(other.ExportSubjectPublicKeyInfo(), data => other.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.False(PeerProof.TryVerify(PeerProof.Create(otherSigner, "text"), "text", out _));
    }

    /// <summary>
    /// An envelope carries one description that is SDP of its type, or one candidate line; anything else is refused.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <param name="valid">Whether it is accepted.</param>
    [Theory]
    [InlineData("{\"offer\":{\"type\":\"offer\",\"sdp\":\"v=0\\r\\nm=application 9\"}}", true)]
    [InlineData("{\"answer\":{\"type\":\"answer\",\"sdp\":\"v=0\\nm=application 9\"}}", true)]
    [InlineData("{\"candidate\":{\"candidate\":\"candidate:1 1 udp 1 192.0.2.1 5000 typ host\",\"sdpMLineIndex\":0,\"sdpMid\":\"0\"}}", true)]
    [InlineData("{\"offer\":{\"type\":\"answer\",\"sdp\":\"v=0\\nm=application 9\"}}", false)]
    [InlineData("{\"offer\":{\"type\":\"offer\",\"sdp\":\"hello\"}}", false)]
    [InlineData("{\"candidate\":{\"candidate\":\"\"}}", false)]
    [InlineData("{\"candidate\":{\"candidate\":\"candidate:1\\r\\na=evil\"}}", false)]
    [InlineData("{\"offer\":{\"type\":\"offer\",\"sdp\":\"v=0\\nm=a\"},\"candidate\":{\"candidate\":\"candidate:1\"}}", false)]
    [InlineData("[]", false)]
    [InlineData("", false)]
    public void Envelope_AcceptsOnlyOneWellFormedSignal(string data, bool valid)
        => Assert.Equal(valid, SignalEnvelope.TryRead(data, out _));

    /// <summary>
    /// A stream over a channel cuts writes into messages of at most 16 KiB and glues them back; the end of the channel
    /// is the end of the stream.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task Stream_CutsAndGluesMessages()
    {
        var token = TestContext.Current.CancellationToken;
        var (a, b) = FakeDataChannel.Pair("1");
        await using var writer = new DataChannelStream(a);
        await using var reader = new DataChannelStream(b);
        var data = RandomNumberGenerator.GetBytes(40_000);

        await writer.WriteAsync(data, token);
        var read = new byte[data.Length];
        await reader.ReadExactlyAsync(read, token);

        Assert.Equal(data, read);
        Assert.Equal([16 * 1024, 16 * 1024, 40_000 - (32 * 1024)], a.SentSizes);

        await writer.DisposeAsync();
        Assert.Equal(0, await reader.ReadAsync(new byte[1], token));
    }

    /// <summary>
    /// A host session accepts the streams of its listener and the streams offered through WebRTC alike.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task CompositeListener_GivesBothWaysIn()
    {
        var token = TestContext.Current.CancellationToken;
        var primary = new QueueListener();
        await using var listener = new CompositePeerListener(primary);
        var peer = PeerId.Parse(new string('a', 64));
        var offered = new PeerStream(peer, StreamPurpose.Control, new MemoryStream(), "WebRTC");
        var accepted = new PeerStream(peer, StreamPurpose.MediaData, new MemoryStream(), "TLS");

        Assert.True(listener.Offer(offered));
        Assert.Same(offered, await listener.AcceptAsync(token));

        primary.Streams.Writer.TryWrite(accepted);
        Assert.Same(accepted, await listener.AcceptAsync(token));
        Assert.Equal(primary.Port, listener.Port);
    }

    /// <summary>
    /// A listener whose streams the test puts in.
    /// </summary>
    private sealed class QueueListener : IPeerListener
    {
        /// <summary>
        /// Gets the streams to hand out.
        /// </summary>
        public System.Threading.Channels.Channel<PeerStream> Streams { get; } = System.Threading.Channels.Channel.CreateUnbounded<PeerStream>();

        /// <inheritdoc />
        public int Port => 47800;

        /// <inheritdoc />
        public async ValueTask<PeerStream> AcceptAsync(CancellationToken cancellationToken) => await Streams.Reader.ReadAsync(cancellationToken);

        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
