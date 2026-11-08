using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;

namespace Golether.Transports.WebRtc;

/// <summary>
/// The way of a guest to a host through the signalling: the first stream meets the host, makes a WebRTC connection,
/// proves the key of this device and checks the key of the host against the invitation; every later stream is one more
/// channel of the same connection. A connection that failed is met again on the next stream.
/// </summary>
public sealed class WebRtcSessionConnector : ISignalConnector, IAsyncDisposable
{
    /// <summary>
    /// The device key of the guest.
    /// </summary>
    private readonly DeviceSigner _signer;

    /// <summary>
    /// Makes the connections.
    /// </summary>
    private readonly IDataLinkFactory _factory;

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
    /// The connections by host and room; a meeting in progress is shared by the streams waiting for it.
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<IDataLink>>> _links = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRtcSessionConnector"/> class.
    /// </summary>
    /// <param name="signer">The device key of the guest.</param>
    /// <param name="factory">Makes the connections.</param>
    /// <param name="options">The limits, or <see langword="null"/> for the defaults.</param>
    /// <param name="timeProvider">The source of time.</param>
    /// <param name="logger">The log.</param>
    public WebRtcSessionConnector(DeviceSigner signer, IDataLinkFactory factory, WebRtcOptions? options = null, TimeProvider? timeProvider = null, ILogger<WebRtcSessionConnector>? logger = null)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _options = options ?? new WebRtcOptions();
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<WebRtcSessionConnector>.Instance;
    }

    /// <inheritdoc />
    public async Task<PeerStream> ConnectAsync(SignalRoute route, PeerId expectedPeer, StreamPurpose purpose, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        var key = route.Signal + "|" + route.Room + "|" + expectedPeer.Value;
        var lazy = _links.GetOrAdd(key, _ => new Lazy<Task<IDataLink>>(() => MeetAsync(key, route, expectedPeer)));

        IDataLink link;
        try
        {
            link = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _links.TryRemove(new KeyValuePair<string, Lazy<Task<IDataLink>>>(key, lazy));
            throw;
        }

        try
        {
            using var timeout = new CancellationTokenSource(_options.ProofTimeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var channel = await link.OpenChannelAsync(((int)purpose).ToString(CultureInfo.InvariantCulture), linked.Token).ConfigureAwait(false);
            return new PeerStream(expectedPeer, purpose, new DataChannelStream(channel), "WebRTC");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The connection does not open channels any more: it is met again on the next stream.
            Forget(key, lazy, link);
            throw new IOException("The WebRTC connection with the host does not answer.");
        }
        catch (IOException)
        {
            Forget(key, lazy, link);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in _links.Values)
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
            {
                await lazy.Value.Result.DisposeAsync().ConfigureAwait(false);
            }
        }

        _links.Clear();
    }

    /// <summary>
    /// Forgets a connection that stopped working and closes it.
    /// </summary>
    /// <param name="key">Its key.</param>
    /// <param name="lazy">Its entry.</param>
    /// <param name="link">The connection.</param>
    private void Forget(string key, Lazy<Task<IDataLink>> lazy, IDataLink link)
    {
        if (_links.TryRemove(new KeyValuePair<string, Lazy<Task<IDataLink>>>(key, lazy)))
        {
            _ = link.DisposeAsync().AsTask();
        }
    }

    /// <summary>
    /// Meets the host through the signalling and makes a proven connection.
    /// </summary>
    /// <param name="key">The key of the connection.</param>
    /// <param name="route">The way to the host.</param>
    /// <param name="expectedPeer">The host identifier pinned by the invitation.</param>
    /// <returns>The connection whose host proved the expected key.</returns>
    /// <exception cref="PeerAuthenticationException">The host proved another key.</exception>
    /// <exception cref="IOException">The host was not reached.</exception>
    private async Task<IDataLink> MeetAsync(string key, SignalRoute route, PeerId expectedPeer)
    {
        using var limit = new CancellationTokenSource(_options.ConnectTimeout + _options.ProofTimeout, _time);
        var client = await SignalClient.ConnectAsync(route.Signal, limit.Token).ConfigureAwait(false);
        var link = _factory.Create(route.StunServer, null);
        EventHandler<IceCandidate> candidates = (_, candidate) => _ = client.SendAsync(Signal(SignalEnvelope.ForCandidate(candidate)), CancellationToken.None);
        try
        {
            await using (client.ConfigureAwait(false))
            {
                await client.MeetAsync(route.Room, limit.Token).ConfigureAwait(false);
                link.CandidateReady += candidates;
                var auth = link.OpenChannelAsync(PeerProof.AUTH_LABEL, limit.Token);
                var offer = await link.CreateOfferAsync(limit.Token).ConfigureAwait(false);
                await client.SendAsync(Signal(SignalEnvelope.ForOffer(offer)), limit.Token).ConfigureAwait(false);
                _ = RelayAsync(client, link, limit.Token);

                var channel = await auth.WaitAsync(_options.ConnectTimeout, _time, limit.Token).ConfigureAwait(false);
                await ProveAsync(channel, link, route.Room, expectedPeer, limit.Token).ConfigureAwait(false);

                // The meeting is over once both keys are proven: the server is no longer needed.
                link.CandidateReady -= candidates;
            }

            link.Failed += (_, _) => ForgetFailed(key, link);
            _logger.LogInformation("Met the host {Host} through the signalling", expectedPeer.ToShortString());
            return link;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            link.CandidateReady -= candidates;
            await link.DisposeAsync().ConfigureAwait(false);
            throw new SignalException("PunchFailed", ex);
        }
        catch
        {
            link.CandidateReady -= candidates;
            await link.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Forgets a connection that failed, if it is still the one kept for its host.
    /// </summary>
    /// <param name="key">Its key.</param>
    /// <param name="link">The connection.</param>
    private void ForgetFailed(string key, IDataLink link)
    {
        if (_links.TryGetValue(key, out var entry) && entry.IsValueCreated && entry.Value.IsCompletedSuccessfully && entry.Value.Result == link)
        {
            Forget(key, entry, link);
        }
    }

    /// <summary>
    /// Proves the key of this device and checks the key of the host.
    /// </summary>
    /// <param name="channel">The channel of the proofs.</param>
    /// <param name="link">The connection.</param>
    /// <param name="room">The room.</param>
    /// <param name="expectedPeer">The host identifier pinned by the invitation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the host is proven.</returns>
    /// <exception cref="PeerAuthenticationException">The host proved another key, or none.</exception>
    private async Task ProveAsync(IDataChannel channel, IDataLink link, string room, PeerId expectedPeer, CancellationToken cancellationToken)
    {
        if (link.LocalDescription is not { } local || link.RemoteDescription is not { } remote)
        {
            throw new IOException("The connection is not described.");
        }

        var text = PeerProof.Text(remote, local, room);
        var reply = ProofExchange.ReadTextAsync(channel, _options.ProofTimeout, _time, cancellationToken);
        channel.SendText(PeerProof.Create(_signer, text));
        var message = await reply.ConfigureAwait(false);
        if (!PeerProof.TryVerify(message, text, out var host) || host != expectedPeer)
        {
            throw new PeerAuthenticationException("The other side proved a different key than the invitation names.");
        }
    }

    /// <summary>
    /// Passes the answer and the candidates of the host to the connection until the meeting ends.
    /// </summary>
    /// <param name="client">The connection with the server.</param>
    /// <param name="link">The connection.</param>
    /// <param name="cancellationToken">Ends the meeting.</param>
    /// <returns>A task that completes when the meeting ends.</returns>
    private async Task RelayAsync(SignalClient client, IDataLink link, CancellationToken cancellationToken)
    {
        var answered = false;
        try
        {
            while (await client.ReceiveAsync(cancellationToken).ConfigureAwait(false) is { } frame)
            {
                if (frame.Type != SignalFrame.SIGNAL || !SignalEnvelope.TryRead(frame.Data, out var envelope))
                {
                    continue;
                }

                if (envelope.Answer is { Sdp: { } answer } && !answered)
                {
                    answered = true;
                    await link.ApplyAnswerAsync(answer, cancellationToken).ConfigureAwait(false);
                }
                else if (envelope.Candidate is { Candidate: { } candidate } item)
                {
                    link.AddCandidate(new IceCandidate(item.SdpMLineIndex ?? 0, candidate));
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogDebug("The meeting with the host ended: {Reason}", ex.Message);
        }
    }

    /// <summary>
    /// Wraps data into a <c>signal</c> frame of the guest.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>The frame.</returns>
    private static SignalFrame Signal(string data) => new() { Type = SignalFrame.SIGNAL, Data = data };
}
