namespace Golether.Media.Conference.Browser;

/// <summary>
/// A STUN or TURN server as a browser takes it (<c>RTCIceServer</c>).
/// </summary>
/// <param name="Url">The address: <c>stun:host:port</c> or <c>turn:host:port?transport=tcp</c>.</param>
/// <param name="Username">The user of a TURN server, or <see langword="null"/>.</param>
/// <param name="Credential">The password of a TURN server, or <see langword="null"/>.</param>
public sealed record BrowserIceServer(string Url, string? Username = null, string? Credential = null)
{
    /// <summary>
    /// Reads the TURN address of the sessions (<c>turn://user:password@host:port?transport=tcp</c>).
    /// </summary>
    /// <param name="turn">The address.</param>
    /// <returns>The server as a browser takes it.</returns>
    /// <exception cref="ArgumentException">The address is not such a TURN address.</exception>
    public static BrowserIceServer FromTurnUri(string turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        if (!Uri.TryCreate(turn, UriKind.Absolute, out var uri) || uri.Scheme != "turn" || uri.Port <= 0 ||
            uri.UserInfo.Split(':') is not [{ Length: > 0 } user, { Length: > 0 } password])
        {
            throw new ArgumentException("A relay address must be turn://user:password@host:port.", nameof(turn));
        }

        return new BrowserIceServer(
            $"turn:{uri.Host}:{uri.Port}?transport=tcp",
            Uri.UnescapeDataString(user),
            Uri.UnescapeDataString(password));
    }
}
