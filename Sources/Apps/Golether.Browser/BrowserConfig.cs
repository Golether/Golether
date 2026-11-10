using System.Text.Json;
using Golether.Core.Networking;

namespace Golether.Browser;

/// <summary>
/// The servers a page works with, from its <c>config.json</c>: one rendezvous server answers the WebSocket of the
/// browsers, STUN and the meetings of the applications.
/// </summary>
/// <param name="SignalUrl">
/// The WebSocket of the signalling (<c>wss://…/signal</c>), or <see langword="null"/> while the project names none:
/// the page then opens, but hosts nothing and joins only invitations that name a signalling of their own.
/// </param>
/// <param name="StunUrl">The STUN server (<c>stun:host:port</c>).</param>
/// <param name="Meet">The rendezvous server written into invitations.</param>
internal sealed record BrowserConfig(string? SignalUrl, string StunUrl, PeerEndpoint Meet)
{
    /// <summary>
    /// The field of the sealed address of the rendezvous server; its presence marks the file of the project page.
    /// </summary>
    private const string RENDEZVOUS = "rendezvous";

    /// <summary>
    /// The field of the signalling: a sealed <c>host:port</c> in the file of the project page, a <c>ws(s)://</c>
    /// address in the file of a developer's computer.
    /// </summary>
    private const string SIGNAL = "signal";

    /// <summary>
    /// Gets the signalling as an address, or <see langword="null"/> when there is none; a session hosted on this page
    /// names it in its invitations.
    /// </summary>
    public Uri? Signal => SignalUrl is null ? null : new(SignalUrl);

    /// <summary>
    /// Reads the configuration from the JSON of <c>config.json</c>. The published page carries the file of the project
    /// page, <c>{"rendezvous":"g1.…","signal":"g1.…"}</c>, with the servers sealed: STUN answers at the rendezvous
    /// server and the signalling, when the file names one, is <c>wss://host:port/signal</c>. A developer's computer
    /// names the servers in the open: <c>signal</c>, <c>stun</c>, <c>meet</c>.
    /// </summary>
    /// <param name="json">The JSON.</param>
    /// <param name="open">Opens a sealed address into <c>host:port</c>, or into an empty string.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="FormatException">The file or a value is not valid.</exception>
    public static async Task<BrowserConfig> ReadAsync(string json, Func<string, Task<string>> open)
    {
        ArgumentNullException.ThrowIfNull(open);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        values[property.Name] = property.Value.GetString() ?? string.Empty;
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            throw new FormatException("config.json is not valid JSON.", ex);
        }

        if (!values.ContainsKey(RENDEZVOUS))
        {
            return Parse(values.GetValueOrDefault(SIGNAL), Read(values, "stun"), Read(values, "meet"));
        }

        var meet = await OpenAsync(open, Read(values, RENDEZVOUS), RENDEZVOUS).ConfigureAwait(true);
        string? signal = null;
        if (values.TryGetValue(SIGNAL, out var sealedSignal))
        {
            signal = SignalAddress.FromEndpoint(await OpenAsync(open, sealedSignal, SIGNAL).ConfigureAwait(true)).AbsoluteUri;
        }

        return Parse(signal, "stun:" + meet, meet.ToString());
    }

    /// <summary>
    /// Checks the values the page read and builds the configuration.
    /// </summary>
    /// <param name="signalUrl">The WebSocket of the signalling, or <see langword="null"/> when there is none.</param>
    /// <param name="stunUrl">The STUN server.</param>
    /// <param name="meet">The rendezvous server as <c>host:port</c>.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="FormatException">A value is not valid.</exception>
    public static BrowserConfig Parse(string? signalUrl, string stunUrl, string meet)
    {
        if (signalUrl is not null && !SignalAddress.TryParse(signalUrl, out _))
        {
            throw new FormatException("The signalling address must be wss://, or ws:// to this computer.");
        }

        if (!stunUrl.StartsWith("stun:", StringComparison.Ordinal))
        {
            throw new FormatException("The STUN address must start with stun:.");
        }

        if (!PeerEndpoint.TryParse(meet, out var endpoint))
        {
            throw new FormatException("The rendezvous server must be host:port.");
        }

        return new BrowserConfig(signalUrl, stunUrl, endpoint);
    }

    /// <summary>
    /// Opens a sealed address of the file.
    /// </summary>
    /// <param name="open">Opens a sealed address.</param>
    /// <param name="text">The sealed text.</param>
    /// <param name="name">The name of the field, for the message.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">The text is not a seal of an address.</exception>
    private static async Task<PeerEndpoint> OpenAsync(Func<string, Task<string>> open, string text, string name)
    {
        var opened = await open(text).ConfigureAwait(true);

        return PeerEndpoint.TryParse(opened, out var endpoint)
            ? endpoint
            : throw new FormatException("The " + name + " of config.json is not a sealed host:port.");
    }

    /// <summary>
    /// Reads a text value of the configuration.
    /// </summary>
    /// <param name="values">The text values of the file.</param>
    /// <param name="name">The name of the value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The value is missing or not a text.</exception>
    private static string Read(Dictionary<string, string> values, string name)
        => values.TryGetValue(name, out var value) ? value : throw new FormatException("config.json has no " + name + ".");
}
