using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Golether.Browser.Interop;

namespace Golether.Browser.Controls;

/// <summary>
/// Types what the keyboard of a phone wrote. Avalonia in a browser takes text only from keys that name a character
/// and from the end of a composition, and the keyboard of a phone sends neither: its keys are "Unidentified" and it
/// writes into the hidden input of Avalonia directly. The page reads that change (<c>page.js</c>) and it is replayed
/// here as the input a real keyboard would give, so the field keeps its own rules (the longest text, undo).
/// </summary>
internal static class SoftKeyboardInput
{
    /// <summary>
    /// Whether the fields are watched already.
    /// </summary>
    private static bool _attached;

    /// <summary>
    /// Starts keeping the hidden input of Avalonia in step with the field that has the focus: its text and its
    /// selection. Avalonia copies the text there but not the moves of the caret, so without this the keyboard of a
    /// phone would edit where the caret stood when the field took the focus.
    /// </summary>
    public static void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        TextBox.TextProperty.Changed.AddClassHandler<TextBox>((field, _) => Sync(field));
        TextBox.SelectionStartProperty.Changed.AddClassHandler<TextBox>((field, _) => Sync(field));
        TextBox.SelectionEndProperty.Changed.AddClassHandler<TextBox>((field, _) => Sync(field));
        InputElement.GotFocusEvent.AddClassHandler<TextBox>((field, _) => Sync(field));
    }

    /// <summary>
    /// Applies a change to a field: selects the replaced stretch, then types the new text over it or deletes it.
    /// </summary>
    /// <param name="field">The field that has the focus.</param>
    /// <param name="edit">The change, counted from the start of the selection.</param>
    public static void Apply(TextBox field, TextEdit edit)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(edit);

        var length = field.Text?.Length ?? 0;
        var anchor = Math.Min(field.SelectionStart, field.SelectionEnd);
        var start = Math.Clamp(anchor + edit.From, 0, length);
        var end = Math.Clamp(anchor + edit.To, start, length);
        if (start == end && edit.Text.Length == 0)
        {
            return;
        }

        field.SelectionStart = start;
        field.SelectionEnd = end;
        if (edit.Text.Length > 0)
        {
            field.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = edit.Text });
        }
        else
        {
            field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Back });
        }
    }

    /// <summary>
    /// Copies the text and the selection of a field into the hidden input, once the field has settled after the
    /// change (Avalonia writes its own copy of the text first).
    /// </summary>
    /// <param name="field">The field that changed.</param>
    private static void Sync(TextBox field)
    {
        if (!field.IsFocused)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!field.IsFocused)
            {
                return;
            }

            var start = Math.Min(field.SelectionStart, field.SelectionEnd);
            var end = Math.Max(field.SelectionStart, field.SelectionEnd);
            PageInterop.SyncSoftKeyboard(field.Text ?? string.Empty, start, end);
        }, DispatcherPriority.Background);
    }
}
