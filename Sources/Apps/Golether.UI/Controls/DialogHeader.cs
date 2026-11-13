using Avalonia;
using Avalonia.Controls.Primitives;

namespace Golether.UI.Controls;

/// <summary>
/// The band at the top of a dialog: the title on a panel of its own, set apart from the text below by a line and marked
/// with the accent. The look lives in the theme; the class <c>error</c> marks the title of an error in red.
/// </summary>
public sealed class DialogHeader : TemplatedControl
{
    /// <summary>
    /// Defines the <see cref="Title"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<DialogHeader, string?>(nameof(Title));

    /// <summary>
    /// Gets or sets the title of the dialog.
    /// </summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
