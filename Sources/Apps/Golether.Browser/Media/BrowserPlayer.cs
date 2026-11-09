using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using Golether.Browser.Interop;
using Golether.Core.Playback;
using Golether.Localization;

namespace Golether.Browser.Media;

/// <summary>
/// The player of the sessions in a browser: the <c>&lt;video&gt;</c> element of the page. The host plays the chosen
/// file in place; a participant plays the film published by <see cref="BrowserStreams"/>. The element plays what the
/// browser decodes and decodes the rest itself; a film it cannot open is refused with a clear reason.
/// </summary>
internal sealed class BrowserPlayer : IPlaybackController
{
    /// <summary>
    /// The number of values in a snapshot of the page.
    /// </summary>
    private const int SNAPSHOT_LENGTH = 7;

    /// <summary>
    /// The beginning of the source of a chosen file: the page plays the file itself, not an address of it.
    /// </summary>
    private const string FILE_SOURCE = "file:";

    /// <summary>
    /// How far ahead the element reads, as the desktop player (its demuxer reads 30 seconds ahead): the most a page
    /// that decodes the film itself reports as loaded.
    /// </summary>
    private static readonly TimeSpan _lookAhead = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The source the element plays, or <see langword="null"/> when nothing is loaded.
    /// </summary>
    private string? _source;

    /// <summary>
    /// The films of a participant.
    /// </summary>
    private readonly BrowserStreams _streams;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserPlayer"/> class.
    /// </summary>
    /// <param name="streams">The films of a participant.</param>
    public BrowserPlayer(BrowserStreams streams)
    {
        _streams = streams ?? throw new ArgumentNullException(nameof(streams));
    }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public PlayerSnapshot GetSnapshot()
    {
        var values = PlayerInterop.Snapshot();
        if (values.Length < SNAPSHOT_LENGTH || values[0] == 0)
        {
            return PlayerSnapshot.Empty;
        }

        var position = Seconds(values[1]);
        var duration = Seconds(values[2]);
        var ahead = values[5] < 0 ? DecodedAhead(position, duration) : Seconds(values[5]) ?? TimeSpan.Zero;

        return new PlayerSnapshot(
            true,
            position,
            duration,
            values[3] != 0,
            values[4] != 0,
            ahead,
            double.IsFinite(values[6]) && values[6] > 0 ? values[6] : 1.0);
    }

    /// <inheritdoc />
    public async Task LoadAsync(Uri source, TimeSpan startPosition, bool paused, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var url = Resolve(source);
        _source = url;

        try
        {
            await PlayerInterop.LoadAsync(url, startPosition.TotalSeconds, paused).ConfigureAwait(false);
        }
        catch (JSException ex)
        {
            throw new InvalidOperationException(Texts.Get("Browser.Error.Format"), ex);
        }
    }

    /// <inheritdoc />
    public Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default)
    {
        PlayerInterop.SetPaused(paused);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        PlayerInterop.Seek(position.TotalSeconds);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetRateAsync(double rate, CancellationToken cancellationToken = default)
    {
        PlayerInterop.SetRate(rate);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _source = null;
        PlayerInterop.Stop();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Unloads the source.
    /// </summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        PlayerInterop.Stop();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Finds the address on the page of a source the session gave.
    /// </summary>
    /// <param name="source">A path of a chosen file or an address of a published film.</param>
    /// <returns>The address the element plays.</returns>
    /// <exception cref="InvalidOperationException">The source is neither.</exception>
    private string Resolve(Uri source)
    {
        if (source.IsFile && BrowserFiles.TryFind(source.LocalPath, out var file))
        {
            return FILE_SOURCE + file.ToString(CultureInfo.InvariantCulture);
        }

        if (_streams.TryGetPageUrl(source, out var pageUrl))
        {
            return pageUrl;
        }

        throw new InvalidOperationException("The source is neither a chosen file nor a published film.");
    }

    /// <summary>
    /// Tells how much is loaded ahead when the element decodes the film itself and the browser cannot say: a chosen
    /// file is here whole, so as much as the element reads ahead; a film of the host as much as the cache of the
    /// session holds from the position on, in the time of the film by its average bitrate, up to the same limit.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <param name="duration">The length of the film.</param>
    /// <returns>The time loaded ahead.</returns>
    private TimeSpan DecodedAhead(TimeSpan? position, TimeSpan? duration)
    {
        if (position is not { } at || duration is not { } length || length <= at)
        {
            return TimeSpan.Zero;
        }

        var ahead = _lookAhead;
        if (_source is { } source &&
            _streams.TryGetCachedAhead(source, at / length, _lookAhead / length, out var cached) &&
            length * cached < ahead)
        {
            ahead = length * cached;
        }

        var remaining = length - at;
        return ahead < remaining ? ahead : remaining;
    }

    /// <summary>
    /// Turns seconds of the page into a time.
    /// </summary>
    /// <param name="seconds">The seconds; NaN or infinity when not known.</param>
    /// <returns>The time, or <see langword="null"/> when not known.</returns>
    private static TimeSpan? Seconds(double seconds)
        => double.IsFinite(seconds) && seconds >= 0 && seconds < TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(seconds) : null;
}
