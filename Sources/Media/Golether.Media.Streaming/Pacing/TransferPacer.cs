namespace Golether.Media.Streaming.Pacing;

/// <summary>
/// Lets the bytes of the file through no faster than a rate, evenly: a token bucket kept as the moment the line is
/// free again. Pieces of the film then do not arrive in bursts that fill the queue of a router ahead of the voices
/// and cameras that share the line.
/// </summary>
/// <remarks>
/// A short burst is allowed, so a sender woken a little late by a coarse timer catches up instead of losing the time.
/// </remarks>
public sealed class TransferPacer
{
    /// <summary>
    /// The smallest rate a pacer accepts, in bytes per second.
    /// </summary>
    public const long MIN_RATE = 16 * 1024;

    /// <summary>
    /// How much time ahead of the clock a burst may take: a little more than the coarsest timer step of the systems.
    /// </summary>
    private static readonly TimeSpan _burst = TimeSpan.FromMilliseconds(30);

    /// <summary>
    /// Waits shorter than this are not waited for: no timer is that precise, and the burst covers them.
    /// </summary>
    private static readonly TimeSpan _shortestWait = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// The source of time.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Guards the state.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The rate in bytes per second, or <see langword="null"/> for no limit.
    /// </summary>
    private long? _rate;

    /// <summary>
    /// The timestamp from which the line is free for the next bytes.
    /// </summary>
    private long _free;

    /// <summary>
    /// The bytes let through since <see cref="TakeUsage"/> was called last.
    /// </summary>
    private long _passed;

    /// <summary>
    /// Whether some bytes had to wait since <see cref="TakeUsage"/> was called last.
    /// </summary>
    private bool _limited;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransferPacer"/> class without a limit.
    /// </summary>
    /// <param name="timeProvider">The source of time, the system one by default.</param>
    public TransferPacer(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _free = _timeProvider.GetTimestamp();
    }

    /// <summary>
    /// Gets or sets the rate in bytes per second, at least <see cref="MIN_RATE"/>, or <see langword="null"/> for no
    /// limit.
    /// </summary>
    public long? Rate
    {
        get
        {
            lock (_gate)
            {
                return _rate;
            }
        }

        set
        {
            lock (_gate)
            {
                if (_rate is null && value is not null)
                {
                    // A limit that starts now does not charge the bytes that went before it.
                    _free = _timeProvider.GetTimestamp();
                }

                _rate = value is { } rate ? Math.Max(MIN_RATE, rate) : null;
            }
        }
    }

    /// <summary>
    /// Waits until the bytes may go.
    /// </summary>
    /// <param name="bytes">The number of bytes about to be sent or asked for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the bytes may go.</returns>
    public ValueTask WaitAsync(int bytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        TimeSpan wait;
        lock (_gate)
        {
            _passed += bytes;
            if (_rate is not { } rate)
            {
                return ValueTask.CompletedTask;
            }

            var now = _timeProvider.GetTimestamp();
            var earliest = now - ToTicks(_burst);
            if (_free < earliest)
            {
                _free = earliest;
            }

            wait = _free > now ? _timeProvider.GetElapsedTime(now, _free) : TimeSpan.Zero;
            _free += (long)Math.Ceiling(bytes * (double)_timeProvider.TimestampFrequency / rate);
            if (wait >= _shortestWait)
            {
                _limited = true;
            }
        }

        return wait < _shortestWait ? ValueTask.CompletedTask : new ValueTask(Task.Delay(wait, _timeProvider, cancellationToken));
    }

    /// <summary>
    /// Returns what went through since the previous call and starts counting again.
    /// </summary>
    /// <returns>The bytes let through and whether some of them had to wait for the rate.</returns>
    public (long Bytes, bool Limited) TakeUsage()
    {
        lock (_gate)
        {
            var usage = (_passed, _limited);
            _passed = 0;
            _limited = false;
            return usage;
        }
    }

    /// <summary>
    /// Converts a time into ticks of the timestamps of the source of time.
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns>The ticks.</returns>
    private long ToTicks(TimeSpan time) => (long)(time.TotalSeconds * _timeProvider.TimestampFrequency);
}
