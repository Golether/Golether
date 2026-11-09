using System.Text.Json;
using Golether.Core.Networking;

namespace Golether.Browser;

/// <summary>
/// The servers a page works with, from its <c>config.json</c>: one rendezvous server answers the WebSocket of the
/// browsers, STUN and the meetings of the applications.
/// </summary>
/// <param name="SignalUrl">The WebSocket of the signalling (<c>wss://…/signal</c>).</param>
/// <param name="StunUrl">The STUN server (<c>stun:host:port</c>).</param>
/// <param name="Meet">The rendezvous server written into invitations.</param>
internal sealed record BrowserConfig(string SignalUrl, string StunUrl, PeerEndpoint Meet)
{
    /// <summary>
    /// Gets the signalling as an address; a session hosted on this page names it in its invitations.
    /// </summary>
    public Uri Signal => new(SignalUrl);

    /// <summary>
    /// Reads the configuration from the JSON of <c>config.json</c> (<c>signal</c>, <c>stun</c>, <c>meet</c>).
    /// </summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="FormatException">The file or a value is not valid.</exception>
    public static BrowserConfig FromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return Parse(Read(root, "signal"), Read(root, "stun"), Read(root, "meet"));
        }
        catch (JsonException ex)
        {
            throw new FormatException("config.json is not valid JSON.", ex);
        }
    }

    /// <summary>
    /// Checks the values the page read and builds the configuration.
    /// </summary>
    /// <param name="signalUrl">The WebSocket of the signalling.</param>
    /// <param name="stunUrl">The STUN server.</param>
    /// <param name="meet">The rendezvous server as <c>host:port</c>.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="FormatException">A value is not valid.</exception>
    public static BrowserConfig Parse(string signalUrl, string stunUrl, string meet)
    {
        if (!SignalAddress.TryParse(signalUrl, out _))
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
    /// Reads a text value of the configuration.
    /// </summary>
    /// <param name="root">The object of the file.</param>
    /// <param name="name">The name of the value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The value is missing or not a text.</exception>
    private static string Read(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : throw new FormatException("config.json has no " + name + ".");
}
