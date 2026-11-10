namespace Golether.Browser.Interop;

/// <summary>
/// A change the keyboard of a phone made to the text being typed: the stretch from <see cref="From"/> to
/// <see cref="To"/> is replaced by <see cref="Text"/>. Both ends count from the start of the selection before the
/// change, so they hold whatever part of the text the hidden input of Avalonia mirrors.
/// </summary>
/// <param name="From">The start of the replaced stretch, from the start of the selection (zero or less).</param>
/// <param name="To">The end of the replaced stretch, from the start of the selection.</param>
/// <param name="Text">The text put in its place; empty for a deletion.</param>
internal sealed record TextEdit(int From, int To, string Text);
