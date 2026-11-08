namespace Golether.Transports.WebRtc;

/// <summary>
/// Makes WebRTC connections with data channels.
/// </summary>
public interface IDataLinkFactory
{
    /// <summary>
    /// Makes a connection.
    /// </summary>
    /// <param name="stunServer">The STUN server (<c>stun://host:port</c>), or <see langword="null"/>.</param>
    /// <param name="turnServer">The TURN server (<c>turn://user:password@host:port</c>), or <see langword="null"/>.</param>
    /// <returns>The connection, not negotiated yet.</returns>
    IDataLink Create(string? stunServer, string? turnServer);
}
