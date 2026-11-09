using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// The elements of the page shown with the canvas of Avalonia (<c>stage.js</c>): the film under the canvas and the
/// pictures of the cameras, copied into the tiles.
/// </summary>
internal static partial class StageInterop
{
    /// <summary>
    /// Gives the film element, created on the first call.
    /// </summary>
    /// <returns>The element.</returns>
    [JSImport("stage.film", JsModule.NAME)]
    public static partial JSObject Film();

    /// <summary>
    /// Moves the layer Avalonia keeps the elements of the page in under its canvas: the controls are drawn over the
    /// film, and the canvas receives the clicks on it.
    /// </summary>
    /// <param name="element">An element placed by Avalonia.</param>
    [JSImport("stage.sendBack", JsModule.NAME)]
    public static partial void SendBack(JSObject element);

    /// <summary>
    /// Copies the current picture of a camera, scaled to fit a box, into a buffer as RGBA.
    /// </summary>
    /// <param name="peer">The participant, or an empty string for the camera of this device.</param>
    /// <param name="maxWidth">The width of the box.</param>
    /// <param name="maxHeight">The height of the box.</param>
    /// <param name="target">The buffer, at least <paramref name="maxWidth"/> × <paramref name="maxHeight"/> × 4 bytes.</param>
    /// <returns>The width × 65536 + the height of the copied picture, or 0 when there is no picture.</returns>
    [JSImport("stage.cameraFrame", JsModule.NAME)]
    public static partial int CameraFrame(string peer, int maxWidth, int maxHeight, [JSMarshalAs<JSType.MemoryView>] Span<byte> target);

    /// <summary>
    /// Stops the elements that played the cameras of participants that left.
    /// </summary>
    /// <param name="peers">The participants still in the session.</param>
    [JSImport("stage.keepCameras", JsModule.NAME)]
    public static partial void KeepCameras(string[] peers);
}
