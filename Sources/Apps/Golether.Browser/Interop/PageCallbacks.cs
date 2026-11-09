using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// What the page around the canvas reports (<c>page.js</c>).
/// </summary>
internal static partial class PageCallbacks
{
    /// <summary>
    /// Raised when the page entered or left the full screen, by a button or by the browser itself (Esc).
    /// </summary>
    public static event EventHandler? FullScreenChanged;

    /// <summary>
    /// The page entered or left the full screen.
    /// </summary>
    [JSExport]
    public static void OnFullScreenChanged() => FullScreenChanged?.Invoke(null, EventArgs.Empty);
}
