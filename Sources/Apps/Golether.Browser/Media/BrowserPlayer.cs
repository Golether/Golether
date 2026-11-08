using System.Runtime.InteropServices.JavaScript;
using Golether.Browser.Interop;
using Golether.Core.Playback;
using Golether.Localization;

namespace Golether.Browser.Media;

/// <summary>
/// The player of the sessions in a browser: the <c>&lt;video&gt;</c> element of the page. The host plays the chosen
/// file in place; a participant plays the film published by <see cref="BrowserStreams"/>. The browser decodes what it
/// can (MP4 and WebM with H.264, VP9, AV1, AAC, Opus); another format is refused with a clear reason.
/// </summary>
internal sealed class BrowserPlayer : IPlaybackController
{
    /// <summary>
    /// The number of values in a snapshot of the page.
    /// </summary>
    private const int SNAPSHOT_LENGTH = 7;

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

        return new PlayerSnapshot(
            true,
            Seconds(values[1]),
            Seconds(values[2]),
            values[3] != 0,
            values[4] != 0,
            Seconds(values[5]) ?? TimeSpan.Zero,
            double.IsFinite(values[6]) && values[6] > 0 ? values[6] : 1.0);
    }

    /// <inheritdoc />
    public async Task LoadAsync(Uri source, TimeSpan startPosition, bool paused, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var url = Resolve(source);

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
            return FileInterop.ObjectUrl(file);
        }

        if (_streams.TryGetPageUrl(source, out var pageUrl))
        {
            return pageUrl;
        }

        throw new InvalidOperationException("The source is neither a chosen file nor a published film.");
    }

    /// <summary>
    /// Turns seconds of the page into a time.
    /// </summary>
    /// <param name="seconds">The seconds; NaN or infinity when not known.</param>
    /// <returns>The time, or <see langword="null"/> when not known.</returns>
    private static TimeSpan? Seconds(double seconds)
        => double.IsFinite(seconds) && seconds >= 0 && seconds < TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(seconds) : null;
}
