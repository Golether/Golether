using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Transports.WebRtc;

namespace Golether.Media.Conference.GStreamer.Tests;

/// <summary>
/// Tests of the data-only WebRTC connections of sessions on a real <c>webrtcbin</c>: two connections in one process,
/// joined directly.
/// </summary>
public sealed class DataLinkTests
{
    /// <summary>
    /// How long a connection may take to be made in a test.
    /// </summary>
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Two connections negotiate, open the channel of the proofs and a stream channel, exchange text and ten megabytes
    /// in both directions unchanged.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task Connections_CarryTextAndStreams()
    {
        var factory = CreateFactory(out var skip);
        Assert.SkipWhen(skip is not null, skip ?? string.Empty);

        await using var offerer = factory!.Create(null, null);
        await using var answerer = factory.Create(null, null);
        var remote = Connect(offerer, answerer);

        var auth = offerer.OpenChannelAsync(PeerProof.AUTH_LABEL, TestContext.Current.CancellationToken);
        await NegotiateAsync(offerer, answerer);
        var local = await auth.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        var accepted = await remote.Task.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        Assert.Equal(PeerProof.AUTH_LABEL, accepted.Label);

        var text = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        accepted.MessageReceived += (_, message) => text.TrySetResult(message.Text ?? "<binary>");
        local.SendText("{\"key\":\"hello\"}");
        Assert.Equal("{\"key\":\"hello\"}", await text.Task.WaitAsync(_timeout, TestContext.Current.CancellationToken));

        var secondRemote = new TaskCompletionSource<IDataChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        answerer.ChannelOpened += (_, channel) =>
        {
            if (channel.Label == "2")
            {
                secondRemote.TrySetResult(channel);
            }
        };
        var data = await offerer.OpenChannelAsync("2", TestContext.Current.CancellationToken).WaitAsync(_timeout, TestContext.Current.CancellationToken);
        await using var a = new DataChannelStream(data);
        await using var b = new DataChannelStream(await secondRemote.Task.WaitAsync(_timeout, TestContext.Current.CancellationToken));

        var payload = RandomNumberGenerator.GetBytes(10 * 1024 * 1024);
        var reply = RandomNumberGenerator.GetBytes(3 * 1024 * 1024);
        var received = ReadExactlyAsync(b, payload.Length);
        var receivedReply = ReadExactlyAsync(a, reply.Length);
        await a.WriteAsync(payload, TestContext.Current.CancellationToken).AsTask().WaitAsync(_timeout, TestContext.Current.CancellationToken);
        await b.WriteAsync(reply, TestContext.Current.CancellationToken).AsTask().WaitAsync(_timeout, TestContext.Current.CancellationToken);

        Assert.Equal(SHA256.HashData(payload), SHA256.HashData(await received.WaitAsync(_timeout, TestContext.Current.CancellationToken)));
        Assert.Equal(SHA256.HashData(reply), SHA256.HashData(await receivedReply.WaitAsync(_timeout, TestContext.Current.CancellationToken)));
    }

    /// <summary>
    /// A description whose fingerprint does not belong to the certificate the other side uses (a server that swapped
    /// it) fails the connection: no channel opens.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task ForeignFingerprint_FailsTheConnection()
    {
        var factory = CreateFactory(out var skip);
        Assert.SkipWhen(skip is not null, skip ?? string.Empty);

        await using var offerer = factory!.Create(null, null);
        await using var answerer = factory.Create(null, null);
        Connect(offerer, answerer);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        offerer.Failed += (_, _) => failed.TrySetResult();

        var auth = offerer.OpenChannelAsync(PeerProof.AUTH_LABEL, TestContext.Current.CancellationToken);
        var offer = await offerer.CreateOfferAsync(TestContext.Current.CancellationToken);
        var answer = await answerer.AnswerAsync(offer, TestContext.Current.CancellationToken);
        await offerer.ApplyAnswerAsync(SwapFingerprint(answer), TestContext.Current.CancellationToken);

        await failed.Task.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<Exception>(() => auth.WaitAsync(_timeout, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Makes the factory of the tests.
    /// </summary>
    /// <param name="skip">Why the test cannot run, or <see langword="null"/>.</param>
    /// <returns>The factory, or <see langword="null"/>.</returns>
    private static GStreamerDataLinkFactory? CreateFactory(out string? skip)
    {
        var bundle = TestBundle.Find();
        skip = TestBundle.SkipReason(bundle);
        if (skip is not null)
        {
            return null;
        }

        var factory = new GStreamerDataLinkFactory(new GStreamerConferenceOptions { BundleRoot = bundle, RegistryFile = TestBundle.RegistryFile }, NullLoggerFactory.Instance);
        skip = factory.IsAvailable ? null : factory.UnavailableReason;
        return factory;
    }

    /// <summary>
    /// Passes the candidates of each connection to the other and catches the first channel the answering side gets.
    /// </summary>
    /// <param name="offerer">The offering connection.</param>
    /// <param name="answerer">The answering connection.</param>
    /// <returns>The first channel of the answering side.</returns>
    private static TaskCompletionSource<IDataChannel> Connect(IDataLink offerer, IDataLink answerer)
    {
        offerer.CandidateReady += (_, candidate) => answerer.AddCandidate(candidate);
        answerer.CandidateReady += (_, candidate) => offerer.AddCandidate(candidate);
        var remote = new TaskCompletionSource<IDataChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        answerer.ChannelOpened += (_, channel) => remote.TrySetResult(channel);
        return remote;
    }

    /// <summary>
    /// Negotiates two connections directly.
    /// </summary>
    /// <param name="offerer">The offering connection.</param>
    /// <param name="answerer">The answering connection.</param>
    /// <returns>A task that completes when both descriptions are applied.</returns>
    private static async Task NegotiateAsync(IDataLink offerer, IDataLink answerer)
    {
        var offer = await offerer.CreateOfferAsync(TestContext.Current.CancellationToken);
        var answer = await answerer.AnswerAsync(offer, TestContext.Current.CancellationToken);
        await offerer.ApplyAnswerAsync(answer, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Replaces the fingerprint of a description with another well-formed one.
    /// </summary>
    /// <param name="sdp">The description.</param>
    /// <returns>The description announcing a certificate nobody has.</returns>
    private static string SwapFingerprint(string sdp)
    {
        var foreign = string.Join(':', RandomNumberGenerator.GetBytes(32).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
        return string.Join('\n', sdp.Split('\n').Select(line => line.StartsWith("a=fingerprint:sha-256 ", StringComparison.Ordinal)
            ? "a=fingerprint:sha-256 " + foreign + (line.EndsWith('\r') ? "\r" : string.Empty)
            : line));
    }

    /// <summary>
    /// Reads a number of bytes from a stream.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="count">How many bytes.</param>
    /// <returns>The bytes.</returns>
    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, TestContext.Current.CancellationToken);
        return buffer;
    }
}
