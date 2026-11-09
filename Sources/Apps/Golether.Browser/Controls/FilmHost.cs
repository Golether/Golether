using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Golether.Browser.Interop;

namespace Golether.Browser.Controls;

/// <summary>
/// The place of the film: Avalonia keeps the film element of the page here, and the page puts it under the canvas, so
/// the strokes, the reactions and the buttons are drawn over the picture and a click on it reaches the view.
/// </summary>
internal sealed class FilmHost : NativeControlHost
{
    /// <summary>
    /// Gives Avalonia the film element and lays it under the canvas once Avalonia placed it.
    /// </summary>
    /// <param name="parent">The handle of the page.</param>
    /// <returns>The handle of the film element.</returns>
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var film = StageInterop.Film();
        Dispatcher.UIThread.Post(() => StageInterop.SendBack(film), DispatcherPriority.Background);
        return new JSObjectControlHandle(film);
    }

    /// <summary>
    /// Keeps the film element: the page has one, and the next session plays in it again.
    /// </summary>
    /// <param name="control">The handle of the film element.</param>
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
    }
}
