using Golether.Core.Data.Enums;
using Golether.Core.Identity;

namespace Golether.Transports;

/// <summary>
/// Opens streams to a host through the signalling of the rendezvous server: a WebRTC connection made directly between
/// the sides, its data channels carrying the streams. This is how a browser host is reached, and how applications meet
/// when their other ways fail.
/// </summary>
public interface ISignalConnector
{
    /// <summary>
    /// Opens a stream to the host of a room, meeting it first if there is no connection with it yet.
    /// </summary>
    /// <param name="route">The way to the host.</param>
    /// <param name="expectedPeer">The host identifier pinned by the invitation.</param>
    /// <param name="purpose">The purpose of the stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authenticated stream.</returns>
    /// <exception cref="PeerAuthenticationException">The other side proved another key.</exception>
    /// <exception cref="IOException">The host was not reached.</exception>
    Task<PeerStream> ConnectAsync(SignalRoute route, PeerId expectedPeer, StreamPurpose purpose, CancellationToken cancellationToken);
}
