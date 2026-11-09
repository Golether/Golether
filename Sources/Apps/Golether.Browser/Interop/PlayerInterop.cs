using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// The film element of the page (<c>player.js</c>, an <c>&lt;avbridge-video&gt;</c> that opens MKV, AVI and the
/// rest, and switches their sound tracks).
/// </summary>
internal static partial class PlayerInterop
{
    /// <summary>
    /// Loads a source and waits until its length is known.
    /// </summary>
    /// <param name="source">The address of the source on the page, or <c>file:&lt;number&gt;</c> for a chosen file.</param>
    /// <param name="start">The position to start at, in seconds.</param>
    /// <param name="paused">Whether to stay paused.</param>
    /// <returns>A task that fails when the browser cannot play the source.</returns>
    [JSImport("player.load", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    public static partial Task LoadAsync(string source, double start, bool paused);

    /// <summary>
    /// Pauses or plays.
    /// </summary>
    /// <param name="paused">Whether to pause.</param>
    [JSImport("player.setPaused", JsModule.NAME)]
    public static partial void SetPaused(bool paused);

    /// <summary>
    /// Moves to a position.
    /// </summary>
    /// <param name="seconds">The position in seconds.</param>
    [JSImport("player.seek", JsModule.NAME)]
    public static partial void Seek(double seconds);

    /// <summary>
    /// Changes the rate; the pitch is kept.
    /// </summary>
    /// <param name="rate">The rate.</param>
    [JSImport("player.setRate", JsModule.NAME)]
    public static partial void SetRate(double rate);

    /// <summary>
    /// Unloads the source.
    /// </summary>
    [JSImport("player.stop", JsModule.NAME)]
    public static partial void Stop();

    /// <summary>
    /// Reads the state of the element: loaded (0/1), position, duration, paused (0/1), buffering (0/1), seconds
    /// loaded ahead, rate. A value that is not known is NaN.
    /// </summary>
    /// <returns>The seven numbers.</returns>
    [JSImport("player.snapshot", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static partial double[] Snapshot();

    /// <summary>
    /// Sets the volume of the film on this device.
    /// </summary>
    /// <param name="fraction">The volume from 0 to 1.</param>
    [JSImport("player.setVolume", JsModule.NAME)]
    public static partial void SetVolume(double fraction);

    /// <summary>
    /// Mutes or unmutes the film on this device.
    /// </summary>
    /// <param name="muted">Whether to mute.</param>
    [JSImport("player.setMuted", JsModule.NAME)]
    public static partial void SetMuted(bool muted);

    /// <summary>
    /// Lists the sound tracks of the film.
    /// </summary>
    /// <returns>A JSON array of <c>{id, language, title, codec, channels, selected}</c>.</returns>
    [JSImport("player.audioTracks", JsModule.NAME)]
    public static partial string AudioTracks();

    /// <summary>
    /// Plays one sound track of the film, on this device only.
    /// </summary>
    /// <param name="id">The track.</param>
    /// <returns>A task that completes when the track plays.</returns>
    [JSImport("player.selectAudioTrack", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    public static partial Task SelectAudioTrackAsync(int id);

    /// <summary>
    /// Gives the shape of the picture.
    /// </summary>
    /// <returns>The width divided by the height, or 0 before the picture is known.</returns>
    [JSImport("player.aspect", JsModule.NAME)]
    public static partial double Aspect();
}
