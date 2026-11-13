using System.Globalization;
using Avalonia.Data.Converters;
using Golether.Localization;
using Golether.UI.Controls;

namespace Golether.Browser.Controls;

/// <summary>
/// Cuts a phrase in two at its first comma, so a narrow page breaks a title there and not before its last word: the
/// first part keeps the comma, the second starts with the word after it. A phrase without a comma is all first part.
/// The first part is given in three pieces around its accent word (<c>Main.Hero.Accent</c>), so the word can be drawn
/// in the accent colour, as the desktop and the project page do.
/// </summary>
internal sealed class PhrasePartConverter : IValueConverter
{
    /// <summary>
    /// What stands between the parts.
    /// </summary>
    private const string SEPARATOR = ", ";

    /// <summary>
    /// The key of the accent word.
    /// </summary>
    private const string ACCENT_KEY = "Main.Hero.Accent";

    /// <summary>
    /// Takes this converter's piece of a phrase.
    /// </summary>
    private readonly Func<string, string> _part;

    /// <summary>
    /// Initializes a new instance of the <see cref="PhrasePartConverter"/> class.
    /// </summary>
    /// <param name="part">Takes this converter's piece of a phrase.</param>
    private PhrasePartConverter(Func<string, string> part) => _part = part;

    /// <summary>
    /// Gets the converter of the first part before its accent word.
    /// </summary>
    public static PhrasePartConverter HeadStart { get; } = new(phrase => SplitHead(phrase).Start);

    /// <summary>
    /// Gets the converter of the accent word of the first part.
    /// </summary>
    public static PhrasePartConverter HeadAccent { get; } = new(phrase => SplitHead(phrase).Accent);

    /// <summary>
    /// Gets the converter of the first part after its accent word, with the comma.
    /// </summary>
    public static PhrasePartConverter HeadEnd { get; } = new(phrase => SplitHead(phrase).End);

    /// <summary>
    /// Gets the converter of the part after the comma.
    /// </summary>
    public static PhrasePartConverter Rest { get; } = new(phrase => Cut(phrase).Tail);

    /// <summary>
    /// Gives one piece of a phrase.
    /// </summary>
    /// <param name="value">The phrase.</param>
    /// <param name="targetType">The type of the target.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>The piece; empty when the phrase has no such piece.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => _part(value as string ?? string.Empty);

    /// <summary>
    /// Not supported: a part never becomes the phrase.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="targetType">The type of the target.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// Cuts a phrase at its first comma.
    /// </summary>
    /// <param name="phrase">The phrase.</param>
    /// <returns>The part up to the comma, with it, and the part after it; all first part without a comma.</returns>
    private static (string Head, string Tail) Cut(string phrase)
    {
        var cut = phrase.IndexOf(SEPARATOR, StringComparison.Ordinal);
        return cut < 0 ? (phrase, string.Empty) : (phrase[..(cut + 1)], phrase[(cut + SEPARATOR.Length)..]);
    }

    /// <summary>
    /// Splits the first part of a phrase around its accent word.
    /// </summary>
    /// <param name="phrase">The phrase.</param>
    /// <returns>The pieces; the whole first part as the start when the word is not in it.</returns>
    private static (string Start, string Accent, string End) SplitHead(string phrase)
        => AccentPartConverter.Split(Cut(phrase).Head, Texts.Get(ACCENT_KEY));
}
