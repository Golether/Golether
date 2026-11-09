using System.Text.Json;
using Golether.Browser.Interop;
using Golether.Core.Data.Enums;
using Golether.Localization;
using Golether.Media.Player;

namespace Golether.Browser.Media;

/// <summary>
/// The settings of the film that concern only this page: the volume and the sound track. Subtitles and tracks from
/// other files belong to the applications.
/// </summary>
internal sealed class BrowserLocalControls : ILocalPlayerControls
{
    /// <summary>
    /// The page reports clicks on the picture to Avalonia directly, so this event is never raised.
    /// </summary>
    public event EventHandler<VideoPointerAction>? VideoPointer
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event EventHandler? TracksChanged;

    /// <inheritdoc />
    public void SetVolume(double percent) => PlayerInterop.SetVolume(Math.Clamp(percent, 0, 100) / 100);

    /// <inheritdoc />
    public void SetMuted(bool muted) => PlayerInterop.SetMuted(muted);

    /// <inheritdoc />
    public IReadOnlyList<MediaTrack> GetTracks() => Parse(PlayerInterop.AudioTracks());

    /// <inheritdoc />
    public void SelectTrack(MediaTrackKind kind, long? id)
    {
        if (kind != MediaTrackKind.Audio || id is not { } track || track is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidOperationException(Texts.Get("Browser.Subtitles.Unavailable"));
        }

        _ = PlayerInterop.SelectAudioTrackAsync((int)track);
    }

    /// <inheritdoc />
    public void AddSubtitleFile(string path) => throw new InvalidOperationException(Texts.Get("Browser.Subtitles.Unavailable"));

    /// <inheritdoc />
    public void AddAudioFile(string path) => throw new InvalidOperationException(Texts.Get("Browser.AppOnly"));

    /// <inheritdoc />
    public double GetVideoAspect() => PlayerInterop.Aspect() is var aspect && double.IsFinite(aspect) && aspect > 0 ? aspect : 0;

    /// <summary>
    /// Passes on that the tracks of the film changed.
    /// </summary>
    public void OnTracksChanged() => TracksChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Reads the list of sound tracks the page gave.
    /// </summary>
    /// <param name="json">The JSON array of <c>player.audioTracks</c>.</param>
    /// <returns>The tracks; empty when the list cannot be read.</returns>
    internal static IReadOnlyList<MediaTrack> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var tracks = new List<MediaTrack>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id) || !id.TryGetInt64(out var number))
                {
                    continue;
                }

                tracks.Add(new MediaTrack(
                    number,
                    MediaTrackKind.Audio,
                    Text(item, "title"),
                    Text(item, "language"),
                    Text(item, "codec"),
                    item.TryGetProperty("channels", out var channels) && channels.TryGetInt32(out var count) ? count : null,
                    false,
                    false,
                    item.TryGetProperty("selected", out var selected) && selected.ValueKind == JsonValueKind.True));
            }

            return tracks;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Reads a text property.
    /// </summary>
    /// <param name="item">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The text, or <see langword="null"/> when it is absent or empty.</returns>
    private static string? Text(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
}
