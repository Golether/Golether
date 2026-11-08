namespace Golether.Media.Conference.Browser.Enums;

/// <summary>
/// The state of the connection with one participant as the browser reports it.
/// </summary>
public enum PeerLinkState
{
    /// <summary>
    /// Looking for a path, or the path was lost and may come back.
    /// </summary>
    Connecting = 0,

    /// <summary>
    /// A path is in use and the DTLS certificate of the other side matched its fingerprint.
    /// </summary>
    Connected = 1,

    /// <summary>
    /// The browser gave up on the connection.
    /// </summary>
    Failed = 2,
}
