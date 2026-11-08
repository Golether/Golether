using Golether.Core.Networking;

namespace Golether.Client.Services;

/// <summary>
/// Finds the rendezvous server of the project, so the user does not have to know its address.
/// </summary>
public interface IRendezvousDirectory
{
    /// <summary>
    /// Asks the page of the project for the address; when the page does not answer, the last address it gave is used.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The address, or <see langword="null"/> when none is known.</returns>
    Task<PeerEndpoint?> FindAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gives the signalling of the project (<c>wss://…/signal</c>) the page named the last time
    /// <see cref="FindAsync"/> read it, so browsers can join the sessions of this device.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The address, or <see langword="null"/> when the page names none.</returns>
    Task<Uri?> FindSignalAsync(CancellationToken cancellationToken);
}
