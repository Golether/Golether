using System.Globalization;
using Golether.Localization;

namespace Golether.Web.Server.Options;

/// <summary>
/// The checks the settings of the server share: a problem is worded in the language of the system and names the
/// configuration key.
/// </summary>
internal static class OptionChecks
{
    /// <summary>
    /// Adds a problem when a number is out of range.
    /// </summary>
    /// <param name="problems">The problems.</param>
    /// <param name="key">The configuration key, for example <c>Rendezvous:Port</c>.</param>
    /// <param name="value">The value.</param>
    /// <param name="min">The smallest allowed value.</param>
    /// <param name="max">The largest allowed value.</param>
    public static void Range(List<string> problems, string key, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            problems.Add(Texts.Format("Server.Error.BadValue", key, value, min, max));
        }
    }

    /// <summary>
    /// Adds a problem when a time is not between one second and one hour.
    /// </summary>
    /// <param name="problems">The problems.</param>
    /// <param name="key">The configuration key.</param>
    /// <param name="value">The value.</param>
    public static void Time(List<string> problems, string key, TimeSpan value)
    {
        if (value < TimeSpan.FromSeconds(1) || value > TimeSpan.FromHours(1))
        {
            problems.Add(Texts.Format("Server.Error.BadValue", key, value.ToString("c", CultureInfo.InvariantCulture), "00:00:01", "01:00:00"));
        }
    }
}
