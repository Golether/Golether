using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// The keyboard of a touch screen (<c>softkeyboard.js</c>): the hidden input of Avalonia the keyboard types into.
/// </summary>
internal static partial class SoftKeyboardInterop
{
    /// <summary>
    /// Puts the text and the selection of the field being typed in into the hidden input, when they differ from it;
    /// does nothing without a touch screen.
    /// </summary>
    /// <param name="text">The text of the field.</param>
    /// <param name="start">The start of the selection.</param>
    /// <param name="end">The end of the selection.</param>
    [JSImport("softKeyboard.sync", JsModule.NAME)]
    public static partial void Sync(string text, int start, int end);
}
