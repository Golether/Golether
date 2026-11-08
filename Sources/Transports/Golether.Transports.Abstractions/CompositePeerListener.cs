using System.Threading.Channels;

namespace Golether.Transports;

/// <summary>
/// A listener that hands out the streams of another listener and the streams offered to it by a second way in (the
/// WebRTC connections of browsers), so a host session accepts both without knowing where a guest came from.
/// </summary>
/// <remarks>One reader accepts at a time, as a host session does.</remarks>
public sealed class CompositePeerListener : IPeerListener
{
    /// <summary>
    /// How many offered streams may wait to be accepted; more are refused.
    /// </summary>
    public const int MAX_WAITING = 64;

    /// <summary>
    /// The listener of the first way in.
    /// </summary>
    private readonly IPeerListener _primary;

    /// <summary>
    /// The streams offered and not accepted yet.
    /// </summary>
    private readonly Channel<PeerStream> _offered = Channel.CreateBounded<PeerStream>(new BoundedChannelOptions(MAX_WAITING)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
    });

    /// <summary>
    /// The accept of the first listener that is still running, kept between calls so no stream of it is lost.
    /// </summary>
    private Task<PeerStream>? _pending;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositePeerListener"/> class.
    /// </summary>
    /// <param name="primary">The listener of the first way in; it is disposed with this one.</param>
    public CompositePeerListener(IPeerListener primary)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
    }

    /// <inheritdoc />
    public int Port => _primary.Port;

    /// <summary>
    /// Offers a stream that came another way.
    /// </summary>
    /// <param name="stream">The authenticated stream.</param>
    /// <returns><see langword="false"/> when too many streams wait or the listener is closed; the caller then closes
    /// the stream.</returns>
    public bool Offer(PeerStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return _offered.Writer.TryWrite(stream);
    }

    /// <inheritdoc />
    public async ValueTask<PeerStream> AcceptAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_offered.Reader.TryRead(out var offered))
            {
                return offered;
            }

            _pending ??= _primary.AcceptAsync(cancellationToken).AsTask();
            var waiting = _offered.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var done = await Task.WhenAny(_pending, waiting).ConfigureAwait(false);
            if (done == waiting)
            {
                if (!await waiting.ConfigureAwait(false))
                {
                    throw new ObjectDisposedException(nameof(CompositePeerListener));
                }

                continue;
            }

            var accepted = _pending;
            _pending = null;
            try
            {
                return await accepted.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The accept belonged to an earlier call that gave up; this one starts its own.
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _offered.Writer.TryComplete();
        while (_offered.Reader.TryRead(out var left))
        {
            await left.DisposeAsync().ConfigureAwait(false);
        }

        await _primary.DisposeAsync().ConfigureAwait(false);
    }
}
