using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Golether.Core.Networking;

/// <summary>
/// The address of the signalling of the rendezvous server (<c>wss://…/signal</c>): where browsers and applications
/// pass each other the descriptions of a WebRTC connection. It comes from invitations and settings, so it is checked
/// before use.
/// </summary>
public static class SignalAddress
{
    /// <summary>
    /// The longest address accepted.
    /// </summary>
    public const int MAX_LENGTH = 256;

    /// <summary>
    /// The path of the WebSocket on the server.
    /// </summary>
    public const string PATH = "/signal";

    /// <summary>
    /// Reads an address: <c>wss://</c>, or <c>ws://</c> to this computer only (for tests); without a user, a query or
    /// a fragment.
    /// </summary>
    /// <param name="text">The address.</param>
    /// <param name="address">The address read.</param>
    /// <returns><see langword="true"/> when the address is acceptable.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Uri? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MAX_LENGTH ||
            !Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return false;
        }

        var secure = uri.Scheme == "wss";
        var local = uri.Scheme == "ws" && (uri.IsLoopback || (IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip)));
        if (!secure && !local)
        {
            return false;
        }

        address = uri;
        return true;
    }

    /// <summary>
    /// Builds the address of the signalling at a server: the project page publishes only its sealed
    /// <c>host:port</c>, the scheme and the path are always the same.
    /// </summary>
    /// <param name="endpoint">The server, as <c>host:port</c>.</param>
    /// <returns>The address <c>wss://host:port/signal</c>.</returns>
    public static Uri FromEndpoint(PeerEndpoint endpoint) => new("wss://" + endpoint + PATH);
}
