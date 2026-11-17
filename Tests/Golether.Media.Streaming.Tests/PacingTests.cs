using Microsoft.Extensions.Time.Testing;
using Golether.Core.Media;
using Golether.Media.Streaming.Pacing;
using Golether.Media.Streaming.Protocol;

namespace Golether.Media.Streaming.Tests;

/// <summary>
/// Tests of the pacing of the file: the token bucket and the controller that keeps the queue on the line short.
/// </summary>
public sealed class PacingTests
{
    /// <summary>
    /// A typical round trip of a line without a queue.
    /// </summary>
    private static readonly TimeSpan _line = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// A pacer without a rate lets everything through at once and still counts it.
    /// </summary>
    [Fact]
    public void Pacer_WithoutRate_DoesNotWait()
    {
        var time = new FakeTimeProvider();
        var pacer = new TransferPacer(time);

        var wait = pacer.WaitAsync(10 * 1024 * 1024, TestContext.Current.CancellationToken);

        Assert.True(wait.IsCompleted);
        Assert.Equal((10L * 1024 * 1024, false), pacer.TakeUsage());
    }

    /// <summary>
    /// With a rate, the bytes after the first ones wait as long as the rate needs to carry what went before them.
    /// </summary>
    [Fact]
    public async Task Pacer_WithRate_SpacesBytesOut()
    {
        var time = new FakeTimeProvider();
        var pacer = new TransferPacer(time) { Rate = 100 * 1024 };
        var token = TestContext.Current.CancellationToken;

        var first = pacer.WaitAsync(100 * 1024, token);
        var second = pacer.WaitAsync(1024, token).AsTask();

        Assert.True(first.IsCompleted);
        Assert.False(second.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(900));
        Assert.False(second.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(100));
        await second.WaitAsync(TimeSpan.FromSeconds(5), token);
        Assert.Equal((101L * 1024, true), pacer.TakeUsage());
        Assert.Equal((0L, false), pacer.TakeUsage());
    }

    /// <summary>
    /// Time that passed without sending is not saved up for a burst beyond a few tens of milliseconds.
    /// </summary>
    [Fact]
    public void Pacer_DoesNotSaveIdleTimeForABurst()
    {
        var time = new FakeTimeProvider();
        var pacer = new TransferPacer(time) { Rate = 100 * 1024 };
        var token = TestContext.Current.CancellationToken;

        time.Advance(TimeSpan.FromMinutes(1));
        var first = pacer.WaitAsync(50 * 1024, token);
        var second = pacer.WaitAsync(50 * 1024, token);

        Assert.True(first.IsCompleted);
        Assert.False(second.IsCompleted);
    }

    /// <summary>
    /// A rate below the smallest one is raised to it, so a pacer never holds the bytes for good.
    /// </summary>
    [Fact]
    public void Pacer_RaisesTooLowRate()
    {
        var pacer = new TransferPacer(new FakeTimeProvider()) { Rate = 1 };

        Assert.Equal(TransferPacer.MIN_RATE, pacer.Rate);
    }

    /// <summary>
    /// While the transfer waits for the rate and the queue stays short, the rate grows.
    /// </summary>
    [Fact]
    public void Controller_GrowsOnAnEmptyQueueWhileLimited()
    {
        var controller = new TransferRateController(new FakeTimeProvider());

        var changed = controller.Update(_line, limited: true);

        Assert.True(changed);
        Assert.True(controller.Rate > TransferRateController.INITIAL_RATE);
        Assert.Equal(TimeSpan.Zero, controller.QueueDelay);
    }

    /// <summary>
    /// A rate nobody waits for does not grow, so the next transfer does not burst out at it.
    /// </summary>
    [Fact]
    public void Controller_DoesNotGrowWhenNotLimited()
    {
        var controller = new TransferRateController(new FakeTimeProvider());

        var changed = controller.Update(_line, limited: false);

        Assert.False(changed);
        Assert.Equal(TransferRateController.INITIAL_RATE, controller.Rate);
    }

    /// <summary>
    /// A queue longer than the target lowers the rate at once, the more the longer it is.
    /// </summary>
    [Fact]
    public void Controller_DropsWhenTheQueueGrows()
    {
        var time = new FakeTimeProvider();
        var controller = new TransferRateController(time);
        controller.Update(_line, limited: true);

        // Two late replies in a row: one alone is taken for noise.
        controller.Update(_line + TimeSpan.FromMilliseconds(400), limited: true);
        var before = controller.Rate;
        controller.Update(_line + TimeSpan.FromMilliseconds(400), limited: true);

        Assert.Equal(TimeSpan.FromMilliseconds(400), controller.QueueDelay);
        Assert.True(controller.Rate <= before * (1 - TransferRateController.MAX_DROP) + 1);
    }

    /// <summary>
    /// A single late reply between quick ones does not count as a queue.
    /// </summary>
    [Fact]
    public void Controller_IgnoresASingleLateReply()
    {
        var controller = new TransferRateController(new FakeTimeProvider());
        controller.Update(_line, limited: true);
        var before = controller.Rate;

        controller.Update(_line + TimeSpan.FromMilliseconds(400), limited: false);

        Assert.Equal(before, controller.Rate);
    }

    /// <summary>
    /// However long the queue stays, the rate does not go below the smallest one.
    /// </summary>
    [Fact]
    public void Controller_KeepsTheSmallestRate()
    {
        var controller = new TransferRateController(new FakeTimeProvider());
        controller.Update(_line, limited: true);

        for (var i = 0; i < 50; i++)
        {
            controller.Update(TimeSpan.FromSeconds(2), limited: true);
        }

        Assert.Equal(TransferRateController.MIN_RATE, controller.Rate);
    }

    /// <summary>
    /// The base of the line follows a new route after the periods that remember the old one are gone.
    /// </summary>
    [Fact]
    public void Controller_ForgetsAnOldBase()
    {
        var time = new FakeTimeProvider();
        var controller = new TransferRateController(time);
        controller.Update(_line, limited: false);
        var slower = TimeSpan.FromMilliseconds(300);

        for (var i = 0; i <= TransferRateController.BASE_PERIODS; i++)
        {
            time.Advance(TransferRateController.BasePeriod);
            controller.Update(slower, limited: false);
        }

        Assert.Equal(TimeSpan.Zero, controller.QueueDelay);
    }

    /// <summary>
    /// Round trips that cannot be measurements of a line are ignored.
    /// </summary>
    [Fact]
    public void Controller_IgnoresImpossibleRoundTrips()
    {
        var controller = new TransferRateController(new FakeTimeProvider());

        Assert.False(controller.Update(TimeSpan.Zero, limited: true));
        Assert.False(controller.Update(TimeSpan.FromMinutes(5), limited: true));
        Assert.Null(controller.QueueDelay);
    }

    /// <summary>
    /// A piece is asked for only when the pacer lets its bytes through.
    /// </summary>
    [Fact]
    public async Task PacedSource_WaitsForThePacer()
    {
        var time = new FakeTimeProvider();
        var media = new MediaDescriptor { FileName = "film.mkv", Length = 4 * 64 * 1024, ChunkSize = 64 * 1024, QuickId = new string('a', 64) };
        var inner = new CountingSource(media);
        var pacer = new TransferPacer(time) { Rate = 64 * 1024 };
        await using var source = new PacedChunkSource(inner, pacer);
        var token = TestContext.Current.CancellationToken;

        await source.GetChunkAsync(0, token);
        var second = source.GetChunkAsync(1, token).AsTask();

        Assert.Equal(1, inner.Requests);
        time.Advance(TimeSpan.FromSeconds(1));
        await second.WaitAsync(TimeSpan.FromSeconds(5), token);
        Assert.Equal(2, inner.Requests);
    }

    /// <summary>
    /// A source that answers at once and counts the requests.
    /// </summary>
    /// <param name="media">The media.</param>
    private sealed class CountingSource(MediaDescriptor media) : IChunkSource
    {
        /// <summary>
        /// The requests so far.
        /// </summary>
        private int _requests;

        /// <summary>
        /// Gets the number of requests so far.
        /// </summary>
        public int Requests => Volatile.Read(ref _requests);

        /// <inheritdoc />
        public MediaDescriptor Media { get; } = media;

        /// <inheritdoc />
        public ValueTask<byte[]> GetChunkAsync(long index, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return ValueTask.FromResult(new byte[Media.GetChunkLength(index)]);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
