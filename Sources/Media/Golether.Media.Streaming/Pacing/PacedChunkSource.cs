using Golether.Core.Media;
using Golether.Media.Streaming.Protocol;

namespace Golether.Media.Streaming.Pacing;

/// <summary>
/// Asks for the pieces of a source no faster than a pacer lets: every piece that has to come over the network waits
/// for its bytes first. The cache in front of it asks for each piece once, so a piece is charged once.
/// </summary>
public sealed class PacedChunkSource : IChunkSource
{
    /// <summary>
    /// The source the pieces come from.
    /// </summary>
    private readonly IChunkSource _inner;

    /// <summary>
    /// The pacer of the downloads.
    /// </summary>
    private readonly TransferPacer _pacer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PacedChunkSource"/> class.
    /// </summary>
    /// <param name="inner">The source the pieces come from.</param>
    /// <param name="pacer">The pacer of the downloads.</param>
    public PacedChunkSource(IChunkSource inner, TransferPacer pacer)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
    }

    /// <inheritdoc />
    public MediaDescriptor Media => _inner.Media;

    /// <inheritdoc />
    public async ValueTask<byte[]> GetChunkAsync(long index, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Media.ChunkCount);

        await _pacer.WaitAsync(Media.GetChunkLength(index), cancellationToken).ConfigureAwait(false);
        return await _inner.GetChunkAsync(index, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
