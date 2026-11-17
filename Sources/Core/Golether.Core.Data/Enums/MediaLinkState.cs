namespace Golether.Core.Data.Enums;

/// <summary>
/// The state of the connection that carries the camera and voice of one participant.
/// </summary>
public enum MediaLinkState
{
    /// <summary>
    /// The connection is up and checked: the camera and voice flow.
    /// </summary>
    Connected = 0,

    /// <summary>
    /// The connection broke or could not be made; it is being made again, and until then neither side sees or hears
    /// the other.
    /// </summary>
    Lost = 1,
}
