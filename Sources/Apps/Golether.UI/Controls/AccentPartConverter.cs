using System.Globalization;
using Avalonia.Data.Converters;
using Golether.Localization;

namespace Golether.UI.Controls;

/// <summary>
/// Takes a part of the title of the start screen around its accent word (<c>Main.Hero.Accent</c>): the text before
/// it, the word itself or the text after it, so the word can be drawn in the accent colour as on the project page.
/// </summary>
/// <remarks>
/// The title stays one text for the browser version, which shows it plainly; the accent word is a key of its own and
/// is found in the title of the same language. A title without it is shown whole, with no accent.
/// </remarks>
public sealed class AccentPartConverter : IValueConverter
{
    /// <summary>
    /// The text before the accent word.
    /// </summary>
    public static readonly AccentPartConverter Start = new(parts => parts.Start);

    /// <summary>
    /// The accent word.
    /// </summary>
    public static readonly AccentPartConverter Accent = new(parts => parts.Accent);

    /// <summary>
    /// The text after the accent word.
    /// </summary>
    public static readonly AccentPartConverter End = new(parts => parts.End);

    /// <summary>
    /// The key of the accent word.
    /// </summary>
    private const string ACCENT_KEY = "Main.Hero.Accent";

    /// <summary>
    /// Picks the part this converter gives.
    /// </summary>
    private readonly Func<(string Start, string Accent, string End), string> _pick;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccentPartConverter"/> class.
    /// </summary>
    /// <param name="pick">Picks the part this converter gives.</param>
    private AccentPartConverter(Func<(string Start, string Accent, string End), string> pick) => _pick = pick;

    /// <summary>
    /// Splits a text around the first place of a word, whatever its case.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="accent">The word.</param>
    /// <returns>The text before the word, the word as the text writes it and the text after it; the whole text first
    /// when the word is not in it.</returns>
    public static (string Start, string Accent, string End) Split(string text, string accent)
    {
        ArgumentNullException.ThrowIfNull(text);

        var at = string.IsNullOrEmpty(accent) ? -1 : text.IndexOf(accent, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return (text, string.Empty, string.Empty);
        }

        return (text[..at], text.Substring(at, accent.Length), text[(at + accent.Length)..]);
    }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => _pick(Split(value as string ?? string.Empty, Texts.Get(ACCENT_KEY)));

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
