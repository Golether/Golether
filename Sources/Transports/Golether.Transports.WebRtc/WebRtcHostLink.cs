using System.Globalization;
using Microsoft.Extensions.Logging;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;

namespace Golether.Transports.WebRtc;

/// <summary>
/// The connection of a host with one guest that came through the signalling: it answers the offer, waits for the
/// proof of the guest's key, answers with its own, then hands every channel of the guest to the session as a stream.
/// A connection that is not proven in time, or stays without a stream of the session for too long, is closed.
/// </summary>
internal sealed class WebRtcHostLink : IAsyncDisposable
{
    /// <summary>
    /// The connection.
    /// </summary>
    private readonly IDataLink _link;

    /// <summary>
    /// The room the proofs sign.
    /// </summary>
    private readonly string _room;

    /// <summary>
    /// The device key of the host.
    /// </summary>
    private readonly DeviceSigner _signer;

    /// <summary>
    /// Takes a stream of the guest; <see langword="false"/> when the session refuses it.
    /// </summary>
    private readonly Func<PeerStream, bool> _offer;

    /// <summary>
    /// Passes data to the guest through the signalling while the meeting lasts.
    /// </summary>
    private readonly Func<string, Task> _send;

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
    /// Completes with the identifier of the guest once its key is proven, or fails.
    /// </summary>
    private readonly TaskCompletionSource<PeerId> _proven = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Guards the state below.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Closes the connection when it is not proven in time, or idle.
    /// </summary>
    private ITimer? _timer;

    /// <summary>
    /// Whether the offer of the guest was answered.
    /// </summary>
    private bool _answered;

    /// <summary>
    /// How many channels the guest opened.
    /// </summary>
    private int _channels;

    /// <summary>
    /// How many streams of the session are open.
    /// </summary>
    private int _streams;

    /// <summary>
    /// Set once the connection is closed.
    /// </summary>
    private bool _closed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRtcHostLink"/> class.
    /// </summary>
    /// <param name="link">The connection.</param>
    /// <param name="room">The room.</param>
    /// <param name="signer">The device key of the host.</param>
    /// <param name="offer">Takes a stream of the guest.</param>
    /// <param name="send">Passes data to the guest through the signalling.</param>
    /// <param name="options">The limits.</param>
    /// <param name="time">The source of time.</param>
    /// <param name="logger">The log.</param>
    public WebRtcHostLink(IDataLink link, string room, DeviceSigner signer, Func<PeerStream, bool> offer, Func<string, Task> send, WebRtcOptions options, TimeProvider time, ILogger logger)
    {
        _link = link;
        _room = room;
        _signer = signer;
        _offer = offer;
        _send = send;
        _options = options;
        _time = time;
        _logger = logger;
        _link.CandidateReady += OnCandidate;
        _link.ChannelOpened += OnChannel;
        _link.Failed += OnFailed;
        _timer = _time.CreateTimer(_ => Expire("not proven in time"), null, options.ConnectTimeout + options.ProofTimeout, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Raised once when the connection is closed.
    /// </summary>
    public event EventHandler? Ended;

    /// <summary>
    /// Gets a value indicating whether the key of the guest is proven.
    /// </summary>
    public bool IsProven => _proven.Task.IsCompletedSuccessfully;

    /// <summary>
    /// Takes the data of a <c>signal</c> frame of the guest; anything malformed is skipped.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>A task that completes when the data is applied.</returns>
    public async Task OnSignalAsync(string? data)
    {
        if (!SignalEnvelope.TryRead(data, out var envelope))
        {
            return;
        }

        if (envelope.Candidate is { Candidate: { } candidate } item)
        {
            _link.AddCandidate(new IceCandidate(item.SdpMLineIndex ?? 0, candidate));
            return;
        }

        if (envelope.Offer is not { Sdp: { } offer })
        {
            return;
        }

        lock (_gate)
        {
            if (_answered || _closed)
            {
                return;
            }

            _answered = true;
        }

        try
        {
            var answer = await _link.AnswerAsync(offer, CancellationToken.None).ConfigureAwait(false);
            await _send(SignalEnvelope.ForAnswer(answer)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning("The offer of a browser guest was not answered: {Reason}", ex.Message);
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _timer?.Dispose();
            _timer = null;
        }

        _proven.TrySetException(new IOException("The connection closed."));
        _link.CandidateReady -= OnCandidate;
        _link.ChannelOpened -= OnChannel;
        _link.Failed -= OnFailed;
        await _link.DisposeAsync().ConfigureAwait(false);
        Ended?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Passes a local candidate to the guest.
    /// </summary>
    /// <param name="sender">The connection.</param>
    /// <param name="candidate">The candidate.</param>
    private void OnCandidate(object? sender, IceCandidate candidate) => _ = SendSafelyAsync(SignalEnvelope.ForCandidate(candidate));

    /// <summary>
    /// Closes the connection that failed.
    /// </summary>
    /// <param name="sender">The connection.</param>
    /// <param name="e">Nothing.</param>
    private void OnFailed(object? sender, EventArgs e) => _ = DisposeAsync().AsTask();

    /// <summary>
    /// Takes a channel the guest opened: the proofs first, then the streams of the session.
    /// </summary>
    /// <param name="sender">The connection.</param>
    /// <param name="channel">The channel.</param>
    private void OnChannel(object? sender, IDataChannel channel)
    {
        bool refuse;
        lock (_gate)
        {
            refuse = _closed || ++_channels > _options.MaxChannelsPerPeer;
        }

        if (refuse)
        {
            _ = channel.DisposeAsync().AsTask();
            return;
        }

        _ = channel.Label == PeerProof.AUTH_LABEL ? ProveAsync(channel) : AcceptStreamAsync(channel);
    }

    /// <summary>
    /// Reads the proof of the guest, checks it and answers with the proof of the host.
    /// </summary>
    /// <param name="channel">The channel of the proofs.</param>
    /// <returns>A task that completes when the proofs are exchanged or refused.</returns>
    private async Task ProveAsync(IDataChannel channel)
    {
        try
        {
            if (IsProven || _link.LocalDescription is not { } local || _link.RemoteDescription is not { } remote)
            {
                throw new IOException("The connection is not described.");
            }

            var text = PeerProof.Text(local, remote, _room);
            var message = await ProofExchange.ReadTextAsync(channel, _options.ProofTimeout, _time, CancellationToken.None).ConfigureAwait(false);
            if (!PeerProof.TryVerify(message, text, out var guest))
            {
                throw new IOException("The guest proved no key.");
            }

            channel.SendText(PeerProof.Create(_signer, text));
            _proven.TrySetResult(guest);
            StartIdle();
            _logger.LogInformation("A guest {Guest} proved its key over WebRTC", guest.ToShortString());
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            _logger.LogWarning("A WebRTC guest is refused: {Reason}", ex.Message);
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Hands a channel of a proven guest to the session as a stream.
    /// </summary>
    /// <param name="channel">The channel; its label is the number of the purpose.</param>
    /// <returns>A task that completes when the stream is handed over or refused.</returns>
    private async Task AcceptStreamAsync(IDataChannel channel)
    {
        PeerId guest;
        try
        {
            guest = await _proven.Task.WaitAsync(_options.ConnectTimeout + _options.ProofTimeout, _time).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (!int.TryParse(channel.Label, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
            !Enum.IsDefined((StreamPurpose)number))
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            return;
        }

        lock (_gate)
        {
            _streams++;
            _timer?.Dispose();
            _timer = null;
        }

        channel.Closed += OnStreamClosed;
        var stream = new PeerStream(guest, (StreamPurpose)number, new DataChannelStream(channel), "WebRTC");
        if (!_offer(stream))
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Counts a stream of the session that closed; the last one starts the wait for an idle connection.
    /// </summary>
    /// <param name="sender">The channel.</param>
    /// <param name="e">Nothing.</param>
    private void OnStreamClosed(object? sender, EventArgs e)
    {
        if (sender is IDataChannel channel)
        {
            channel.Closed -= OnStreamClosed;
        }

        lock (_gate)
        {
            _streams--;
        }

        StartIdle();
    }

    /// <summary>
    /// Starts the wait after which a connection without streams of the session is closed.
    /// </summary>
    private void StartIdle()
    {
        lock (_gate)
        {
            if (_closed || _streams > 0)
            {
                return;
            }

            _timer?.Dispose();
            _timer = _time.CreateTimer(_ => Expire("idle"), null, _options.IdleTimeout, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Closes the connection when its wait ran out.
    /// </summary>
    /// <param name="reason">Why, for the log.</param>
    private void Expire(string reason)
    {
        lock (_gate)
        {
            if (_closed || (reason == "idle" && _streams > 0))
            {
                return;
            }
        }

        _logger.LogInformation("A WebRTC connection of a guest is closed: {Reason}", reason);
        _ = DisposeAsync().AsTask();
    }

    /// <summary>
    /// Sends data to the guest without letting a failure escape: the meeting may be over.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>A task that completes when the data was sent or dropped.</returns>
    private async Task SendSafelyAsync(string data)
    {
        try
        {
            await _send(data).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogDebug("A signal to a guest was dropped: {Reason}", ex.Message);
        }
    }
}
