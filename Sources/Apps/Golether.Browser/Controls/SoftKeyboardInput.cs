using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Golether.Browser.Interop;

namespace Golether.Browser.Controls;

/// <summary>
/// The field side of the keyboard of a touch screen (<c>softkeyboard.js</c>): the change the keyboard made to the hidden
/// input of Avalonia is made in the field at once, in the same event, so the two never differ; and when the field
/// changes on its own (it took the focus, a button filled it), its text and selection are put into the input.
/// </summary>
internal static class SoftKeyboardInput
{
    /// <summary>
    /// Finds the field that has the focus, or <see langword="null"/>.
    /// </summary>
    private static Func<TextBox?>? _focusedField;

    /// <summary>
    /// Starts following the keyboard and the fields.
    /// </summary>
    /// <param name="focusedField">Finds the field that has the focus.</param>
    public static void Attach(Func<TextBox?> focusedField)
    {
        ArgumentNullException.ThrowIfNull(focusedField);
        if (_focusedField is not null)
        {
            return;
        }

        _focusedField = focusedField;
        SoftKeyboardCallbacks.TextEdited += (_, edit) => Apply(edit);
        SoftKeyboardCallbacks.SelectionMoved += (_, selection) => Select(selection.Start, selection.End);
        TextBox.TextProperty.Changed.AddClassHandler<TextBox>((field, _) => Sync(field));
        TextBox.SelectionStartProperty.Changed.AddClassHandler<TextBox>((field, _) => Sync(field));
        TextBox.SelectionEndProperty.Changed.AddClassHandler<TextBox>((field, _) => Sync(field));
        InputElement.GotFocusEvent.AddClassHandler<TextBox>((field, _) => Sync(field));
    }

    /// <summary>
    /// Makes the change of the keyboard in the field: the stretch is selected and typed over or deleted, the way a real
    /// keyboard would, so the field keeps its own rules (the longest text, undo); then the selection is put where the
    /// keyboard left it.
    /// </summary>
    /// <param name="edit">The change.</param>
    private static void Apply(TextEdit edit)
    {
        if (_focusedField?.Invoke() is not { } field)
        {
            return;
        }

        var length = field.Text?.Length ?? 0;
        var start = Math.Clamp(edit.From, 0, length);
        var end = Math.Clamp(edit.To, start, length);
        field.SelectionStart = start;
        field.SelectionEnd = end;
        if (edit.Text.Length > 0)
        {
            field.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = edit.Text });
        }
        else if (end > start)
        {
            field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Back });
        }

        Select(edit.SelectionStart, edit.SelectionEnd);
    }

    /// <summary>
    /// Puts the selection of the field where the keyboard put the one of the input.
    /// </summary>
    /// <param name="start">The start of the selection.</param>
    /// <param name="end">The end of the selection.</param>
    private static void Select(int start, int end)
    {
        if (_focusedField?.Invoke() is not { } field)
        {
            return;
        }

        var length = field.Text?.Length ?? 0;
        field.SelectionStart = Math.Clamp(start, 0, length);
        field.SelectionEnd = Math.Clamp(end, 0, length);
    }

    /// <summary>
    /// Puts the text and the selection of a field into the input once the field has settled; the page leaves the input
    /// alone when they are equal already, as they are after every change of the keyboard.
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
            SoftKeyboardInterop.Sync(field.Text ?? string.Empty, start, end);
        }, DispatcherPriority.Background);
    }
}
