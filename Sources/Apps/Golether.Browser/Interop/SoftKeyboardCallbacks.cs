using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// What the keyboard of a touch screen did to the hidden input of Avalonia (<c>softkeyboard.js</c>), for the field
/// being typed in to do the same at once.
/// </summary>
internal static partial class SoftKeyboardCallbacks
{
    /// <summary>
    /// Raised when the keyboard replaced a stretch of the text.
    /// </summary>
    public static event EventHandler<TextEdit>? TextEdited;

    /// <summary>
    /// Raised when the keyboard moved the caret or the selection without changing the text.
    /// </summary>
    public static event EventHandler<(int Start, int End)>? SelectionMoved;

    /// <summary>
    /// The keyboard replaced a stretch of the text.
    /// </summary>
    /// <param name="from">The start of the replaced stretch in the text.</param>
    /// <param name="to">The end of the replaced stretch in the text.</param>
    /// <param name="text">The text put in its place.</param>
    /// <param name="selectionStart">The start of the selection after the change.</param>
    /// <param name="selectionEnd">The end of the selection after the change.</param>
    [JSExport]
    public static void OnTextEdit(int from, int to, string text, int selectionStart, int selectionEnd)
        => TextEdited?.Invoke(null, new TextEdit(from, to, text ?? string.Empty, selectionStart, selectionEnd));

    /// <summary>
    /// The keyboard moved the caret or the selection.
    /// </summary>
    /// <param name="start">The start of the selection.</param>
    /// <param name="end">The end of the selection.</param>
    [JSExport]
    public static void OnSelect(int start, int end) => SelectionMoved?.Invoke(null, (start, end));
}
