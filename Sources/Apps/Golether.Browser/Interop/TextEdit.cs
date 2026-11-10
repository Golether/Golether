namespace Golether.Browser.Interop;

/// <summary>
/// A change the keyboard of a touch screen made to the text being typed: the stretch from <see cref="From"/> to
/// <see cref="To"/> is replaced by <see cref="Text"/>, and the selection ends up where the keyboard left it. The
/// positions count in the text of the field, which the hidden input of Avalonia holds whole.
/// </summary>
/// <param name="From">The start of the replaced stretch.</param>
/// <param name="To">The end of the replaced stretch.</param>
/// <param name="Text">The text put in its place; empty for a deletion.</param>
/// <param name="SelectionStart">The start of the selection after the change.</param>
/// <param name="SelectionEnd">The end of the selection after the change.</param>
internal sealed record TextEdit(int From, int To, string Text, int SelectionStart, int SelectionEnd);
