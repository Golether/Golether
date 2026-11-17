using Golether.Core.Data.Enums;
using Golether.Core.Identity;

namespace Golether.Media.Conference;

/// <summary>
/// The connection that carries the camera and voice of a participant came up, broke or came back.
/// </summary>
/// <param name="Peer">The participant.</param>
/// <param name="State">The new state.</param>
/// <param name="WasLost">Whether the connection had been lost before it came up, so it came back rather than up.</param>
public sealed record MediaLinkChange(PeerId Peer, MediaLinkState State, bool WasLost);
