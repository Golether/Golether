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
    /// Raised when the keyboard of a phone changed the text being typed (see <c>page.js</c>, the keyboard of a touch
    /// screen).
    /// </summary>
    public static event EventHandler<TextEdit>? TextEdited;

    /// <summary>
    /// The page entered or left the full screen.
    /// </summary>
    [JSExport]
    public static void OnFullScreenChanged() => FullScreenChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>
    /// The keyboard of a phone replaced a stretch of the text being typed.
    /// </summary>
    /// <param name="from">The start of the stretch, from the start of the selection before the change.</param>
    /// <param name="to">The end of the stretch, from the start of the selection before the change.</param>
    /// <param name="text">The text put in its place.</param>
    [JSExport]
    public static void OnTextEdit(int from, int to, string text) => TextEdited?.Invoke(null, new TextEdit(from, to, text ?? string.Empty));
}
