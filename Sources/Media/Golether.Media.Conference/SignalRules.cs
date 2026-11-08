using System.Globalization;
using System.Text.RegularExpressions;

namespace Golether.Media.Conference;

/// <summary>
/// The shape of the signals two participants exchange to connect their cameras: every implementation checks and
/// writes them the same way, so a browser and an application understand each other.
/// </summary>
public static partial class SignalRules
{
    /// <summary>
    /// The maximum size of a signaling payload.
    /// </summary>
    public const int MAX_SIGNAL_LENGTH = 64 * 1024;

    /// <summary>
    /// The longest m-line index a candidate may name, in digits.
    /// </summary>
    private const int MAX_LINE_DIGITS = 5;

    /// <summary>
    /// Checks the shape of signaling data before anything is created for it.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="payload">The payload.</param>
    /// <exception cref="FormatException">The data is invalid.</exception>
    public static void Validate(string kind, string payload)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length > MAX_SIGNAL_LENGTH)
        {
            throw new FormatException("The signaling payload is too large.");
        }

        switch (kind)
        {
            case SignalKinds.OFFER or SignalKinds.ANSWER when payload.StartsWith("v=0", StringComparison.Ordinal) && payload.Contains("\nm=", StringComparison.Ordinal):
                return;
            case SignalKinds.OFFER or SignalKinds.ANSWER:
                throw new FormatException("The session description is not valid SDP.");
            case SignalKinds.CANDIDATE:
                if (!TryParseCandidate(payload, out _, out _))
                {
                    throw new FormatException("The ICE candidate is malformed.");
                }

                return;
            default:
                throw new FormatException($"Unknown signaling kind '{kind}'.");
        }
    }

    /// <summary>
    /// Writes a candidate as it travels: <c>&lt;m-line index&gt;\n&lt;candidate&gt;</c>.
    /// </summary>
    /// <param name="line">The m-line index.</param>
    /// <param name="candidate">The candidate line, starting with <c>candidate:</c>.</param>
    /// <returns>The payload.</returns>
    public static string FormatCandidate(uint line, string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return line.ToString(CultureInfo.InvariantCulture) + "\n" + candidate;
    }

    /// <summary>
    /// Reads a candidate payload.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="line">The m-line index.</param>
    /// <param name="candidate">The candidate line.</param>
    /// <returns><see langword="true"/> when the payload is a well-formed candidate.</returns>
    public static bool TryParseCandidate(string payload, out uint line, out string candidate)
    {
        ArgumentNullException.ThrowIfNull(payload);
        line = 0;
        candidate = string.Empty;

        var separator = payload.IndexOf('\n');
        if (separator <= 0 || separator > MAX_LINE_DIGITS || payload.Contains('\r'))
        {
            return false;
        }

        var digits = payload.AsSpan(0, separator);
        var text = payload[(separator + 1)..];
        foreach (var digit in digits)
        {
            if (!char.IsAsciiDigit(digit))
            {
                return false;
            }
        }

        if (!text.StartsWith("candidate:", StringComparison.Ordinal) ||
            !uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out line))
        {
            line = 0;
            return false;
        }

        candidate = text;
        return true;
    }

    /// <summary>
    /// Extracts the session identifier from the <c>o=</c> line of an SDP: a new identifier means the other side made
    /// a new connection.
    /// </summary>
    /// <param name="sdp">The SDP.</param>
    /// <returns>The identifier, or <see langword="null"/> when the SDP has no origin line.</returns>
    public static string? ParseSession(string sdp)
    {
        ArgumentNullException.ThrowIfNull(sdp);
        var match = OriginPattern().Match(sdp);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Matches the origin line of an SDP and captures its session identifier.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^o=\S+ ([0-9]{1,20}) [0-9]{1,20} ", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex OriginPattern();
}
