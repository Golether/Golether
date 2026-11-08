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
}
