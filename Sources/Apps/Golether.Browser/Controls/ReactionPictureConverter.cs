using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Golether.Browser.Controls;

/// <summary>
/// Turns a reaction into its picture (Noto Emoji): the canvas of a page has no colour emoji font, so a glyph would
/// be drawn as an empty box.
/// </summary>
internal sealed class ReactionPictureConverter : IValueConverter
{
    /// <summary>
    /// The pictures already loaded, by reaction.
    /// </summary>
    private static readonly Dictionary<string, Bitmap?> _pictures = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static ReactionPictureConverter Instance { get; } = new();

    /// <summary>
    /// Gives the picture of a reaction.
    /// </summary>
    /// <param name="value">The reaction.</param>
    /// <param name="targetType">The type of the target.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>The picture, or <see langword="null"/> for an unknown reaction.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string { Length: > 0 } reaction ? Load(reaction) : null;

    /// <summary>
    /// Not supported: a picture never becomes a reaction.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="targetType">The type of the target.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>Never returns.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// Gives the name of the picture of a reaction: its first code point in hex, as Noto Emoji names its files.
    /// </summary>
    /// <param name="reaction">The reaction.</param>
    /// <returns>The name without the extension.</returns>
    internal static string FileName(string reaction)
        => char.ConvertToUtf32(reaction, 0).ToString("x", CultureInfo.InvariantCulture);

    /// <summary>
    /// Loads the picture of a reaction once.
    /// </summary>
    /// <param name="reaction">The reaction.</param>
    /// <returns>The picture, or <see langword="null"/> when there is none.</returns>
    private static Bitmap? Load(string reaction)
    {
        if (_pictures.TryGetValue(reaction, out var known))
        {
            return known;
        }

        var uri = new Uri($"avares://Golether.Browser/Assets/Reactions/{FileName(reaction)}.png");
        Bitmap? picture = null;
        if (AssetLoader.Exists(uri))
        {
            using var stream = AssetLoader.Open(uri);
            picture = new Bitmap(stream);
        }

        _pictures[reaction] = picture;
        return picture;
    }
}
