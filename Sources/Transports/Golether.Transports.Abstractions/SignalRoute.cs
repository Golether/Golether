namespace Golether.Transports;

/// <summary>
/// The way to a host through the signalling, as an invitation gives it.
/// </summary>
/// <param name="Signal">The signalling (<c>wss://…/signal</c>).</param>
/// <param name="Room">The room of the host.</param>
/// <param name="StunServer">The STUN server of the rendezvous server (<c>stun://host:port</c>), or
/// <see langword="null"/>.</param>
public sealed record SignalRoute(Uri Signal, string Room, string? StunServer);
