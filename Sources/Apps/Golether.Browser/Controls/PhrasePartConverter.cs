using System.Globalization;
using Avalonia.Data.Converters;

namespace Golether.Browser.Controls;

/// <summary>
/// Cuts a phrase in two at its first comma, so a narrow page breaks a title there and not before its last word: the
/// first part keeps the comma, the second starts with the word after it. A phrase without a comma is all first part.
/// </summary>
internal sealed class PhrasePartConverter : IValueConverter
{
    /// <summary>
    /// What stands between the parts.
    /// </summary>
    private const string SEPARATOR = ", ";

    /// <summary>
    /// Initializes a new instance of the <see cref="PhrasePartConverter"/> class.
    /// </summary>
    /// <param name="tail">Whether the converter gives the part after the comma.</param>
    private PhrasePartConverter(bool tail) => Tail = tail;

    /// <summary>
    /// Gets the converter of the part up to the comma, with the comma.
    /// </summary>
    public static PhrasePartConverter Head { get; } = new(tail: false);

    /// <summary>
    /// Gets the converter of the part after the comma.
    /// </summary>
    public static PhrasePartConverter Rest { get; } = new(tail: true);

    /// <summary>
    /// Gets a value indicating whether this converter gives the part after the comma.
    /// </summary>
    public bool Tail { get; }

    /// <summary>
    /// Gives one part of a phrase.
    /// </summary>
    /// <param name="value">The phrase.</param>
    /// <param name="targetType">The type of the target.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>The part; empty when the phrase has no such part.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var phrase = value as string ?? string.Empty;
        var cut = phrase.IndexOf(SEPARATOR, StringComparison.Ordinal);
        if (cut < 0)
        {
            return Tail ? string.Empty : phrase;
        }

        return Tail ? phrase[(cut + SEPARATOR.Length)..] : phrase[..(cut + 1)];
    }

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
}
