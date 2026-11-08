using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// The <c>&lt;video&gt;</c> element of the page (<c>player.js</c>).
/// </summary>
internal static partial class PlayerInterop
{
    /// <summary>
    /// Loads a source and waits until its length and size are known.
    /// </summary>
    /// <param name="url">The address of the source on the page.</param>
    /// <param name="start">The position to start at, in seconds.</param>
    /// <param name="paused">Whether to stay paused.</param>
    /// <returns>A task that fails when the browser cannot play the source.</returns>
    [JSImport("player.load", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    public static partial Task LoadAsync(string url, double start, bool paused);

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
}
