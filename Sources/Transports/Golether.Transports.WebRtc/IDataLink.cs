namespace Golether.Transports.WebRtc;

/// <summary>
/// A WebRTC connection that carries only data channels: the connection of a session with a browser, or between
/// applications that could not reach each other otherwise.
/// </summary>
/// <remarks>
/// An implementation opens no channel before the DTLS certificate of the other side matched the fingerprint of the
/// description it signalled: only then does a signature over the fingerprints (<see cref="PeerProof"/>) prove who is on
/// the other end.
/// </remarks>
public interface IDataLink : IAsyncDisposable
{
    /// <summary>
    /// Raised for every local candidate to pass to the other side.
    /// </summary>
    event EventHandler<IceCandidate>? CandidateReady;

    /// <summary>
    /// Raised for a channel the other side opened, once it is open and the connection verified.
    /// </summary>
    event EventHandler<IDataChannel>? ChannelOpened;

    /// <summary>
    /// Raised once when the connection failed, was refused or closed.
    /// </summary>
    event EventHandler? Failed;

    /// <summary>
    /// Gets the local description once it is made.
    /// </summary>
    string? LocalDescription { get; }

    /// <summary>
    /// Gets the description of the other side once it is applied.
    /// </summary>
    string? RemoteDescription { get; }

    /// <summary>
    /// Opens a channel. The first one is opened before the offer, so the offer carries the data section.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The channel once it is open and the connection verified.</returns>
    Task<IDataChannel> OpenChannelAsync(string label, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the offer of the offering side.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The description.</returns>
    Task<string> CreateOfferAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Applies the offer of the other side and makes the answer.
    /// </summary>
    /// <param name="offer">The description of the other side.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer.</returns>
    Task<string> AnswerAsync(string offer, CancellationToken cancellationToken);

    /// <summary>
    /// Applies the answer of the other side.
    /// </summary>
    /// <param name="answer">The description of the other side.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the answer is applied.</returns>
    Task ApplyAnswerAsync(string answer, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a candidate of the other side.
    /// </summary>
    /// <param name="candidate">The candidate.</param>
    void AddCandidate(IceCandidate candidate);
}
