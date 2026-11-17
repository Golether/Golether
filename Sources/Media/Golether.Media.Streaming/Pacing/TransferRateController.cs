namespace Golether.Media.Streaming.Pacing;

/// <summary>
/// Chooses how fast the file may move so that the line keeps room for the voices and cameras: a controller driven by
/// the delay of the queue, in the manner of LEDBAT (RFC 6817).
/// </summary>
/// <remarks>
/// <para>
/// A transfer that only reacts to losses fills the buffer of a router before it slows down; everything else waits in
/// the same queue, and voice packets that come later than the jitter buffer of the receiver are thrown away. Here the
/// round trip of the small control messages is measured instead: its smallest value over the last minutes is the
/// line itself, anything above it is the queue. While the queue stays below <see cref="TargetDelay"/> the rate grows,
/// above it the rate drops at once, the more the further it is above.
/// </para>
/// <para>
/// The rate grows only while the transfer really waited for it: a rate nobody uses would otherwise grow without a
/// limit and let the next transfer burst out at it.
/// </para>
/// </remarks>
public sealed class TransferRateController
{
    /// <summary>
    /// The lowest rate, in bytes per second (1 Mbit/s): the film keeps moving even on a busy line.
    /// </summary>
    public const long MIN_RATE = 128 * 1024;

    /// <summary>
    /// The highest rate, in bytes per second: far above any line, so in effect no limit.
    /// </summary>
    public const long MAX_RATE = 1024L * 1024 * 1024;

    /// <summary>
    /// The rate before the first measurement, in bytes per second (8 Mbit/s): below most lines, so the first pieces do
    /// not flood a slow one.
    /// </summary>
    public const long INITIAL_RATE = 1024 * 1024;

    /// <summary>
    /// The smallest growth of the rate in one step, in bytes per second.
    /// </summary>
    public const long RATE_STEP = 64 * 1024;

    /// <summary>
    /// The share by which the rate grows in one step on an empty queue.
    /// </summary>
    public const double GROWTH = 0.25;

    /// <summary>
    /// The largest share by which the rate drops in one step.
    /// </summary>
    public const double MAX_DROP = 0.5;

    /// <summary>
    /// The number of the latest round trips whose smallest one is taken as the current: one late reply is noise.
    /// </summary>
    public const int CURRENT_SAMPLES = 2;

    /// <summary>
    /// The number of periods whose smallest round trips make the base of the line.
    /// </summary>
    public const int BASE_PERIODS = 10;

    /// <summary>
    /// The longest round trip taken into account: anything longer is not a measurement of the line.
    /// </summary>
    public static readonly TimeSpan MaxRoundTrip = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The delay of the queue the controller keeps to: well below the jitter buffer of the voices (300 ms).
    /// </summary>
    public static readonly TimeSpan TargetDelay = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// The length of one period of the base history.
    /// </summary>
    public static readonly TimeSpan BasePeriod = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The source of time.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The smallest round trip of each of the latest periods, the newest last.
    /// </summary>
    private readonly LinkedList<(long Start, TimeSpan Smallest)> _base = new();

    /// <summary>
    /// The latest round trips, the newest last.
    /// </summary>
    private readonly Queue<TimeSpan> _current = new();

    /// <summary>
    /// Guards the state.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The rate in bytes per second.
    /// </summary>
    private long _rate = INITIAL_RATE;

    /// <summary>
    /// The latest delay of the queue, or <see langword="null"/> before the first measurement.
    /// </summary>
    private TimeSpan? _queueDelay;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransferRateController"/> class.
    /// </summary>
    /// <param name="timeProvider">The source of time, the system one by default.</param>
    public TransferRateController(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets the rate in bytes per second.
    /// </summary>
    public long Rate
    {
        get
        {
            lock (_gate)
            {
                return _rate;
            }
        }
    }

    /// <summary>
    /// Gets the latest delay of the queue on the line, or <see langword="null"/> before the first measurement.
    /// </summary>
    public TimeSpan? QueueDelay
    {
        get
        {
            lock (_gate)
            {
                return _queueDelay;
            }
        }
    }

    /// <summary>
    /// Takes a measured round trip and chooses the rate.
    /// </summary>
    /// <param name="roundTrip">The round trip of a control message, without the time the other side held it.</param>
    /// <param name="limited">Whether the transfer had to wait for the rate since the previous measurement.</param>
    /// <returns><see langword="true"/> when the rate changed.</returns>
    public bool Update(TimeSpan roundTrip, bool limited)
    {
        if (roundTrip <= TimeSpan.Zero || roundTrip > MaxRoundTrip)
        {
            return false;
        }

        lock (_gate)
        {
            var queue = MeasureQueue(roundTrip);
            _queueDelay = queue;

            var rate = _rate;
            if (queue > TargetDelay)
            {
                var excess = Math.Min(1, (queue - TargetDelay) / TargetDelay);
                rate = (long)(rate * (1 - (MAX_DROP * Math.Max(excess, 0.2))));
            }
            else if (limited)
            {
                var room = (TargetDelay - queue) / TargetDelay;
                rate += Math.Max(RATE_STEP, (long)(rate * GROWTH * room));
            }

            rate = Math.Clamp(rate, MIN_RATE, MAX_RATE);
            var changed = rate != _rate;
            _rate = rate;
            return changed;
        }
    }

    /// <summary>
    /// Adds a round trip to the history and returns the delay of the queue: the current round trip above the base.
    /// </summary>
    /// <param name="roundTrip">The round trip.</param>
    /// <returns>The delay of the queue.</returns>
    private TimeSpan MeasureQueue(TimeSpan roundTrip)
    {
        var now = _timeProvider.GetTimestamp();
        if (_base.Last is { } last && _timeProvider.GetElapsedTime(last.Value.Start, now) < BasePeriod)
        {
            last.Value = (last.Value.Start, roundTrip < last.Value.Smallest ? roundTrip : last.Value.Smallest);
        }
        else
        {
            _base.AddLast((now, roundTrip));
            if (_base.Count > BASE_PERIODS)
            {
                _base.RemoveFirst();
            }
        }

        _current.Enqueue(roundTrip);
        if (_current.Count > CURRENT_SAMPLES)
        {
            _current.Dequeue();
        }

        var baseline = _base.Min(p => p.Smallest);
        var current = _current.Min();
        return current - baseline;
    }
}
