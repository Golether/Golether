namespace Golether.Transports.WebRtc.Tests;

/// <summary>
/// A WebRTC connection that does what the test tells: its descriptions are given, its channels are opened by hand.
/// </summary>
internal sealed class FakeDataLink : IDataLink
{
    /// <inheritdoc />
    public event EventHandler<IceCandidate>? CandidateReady;

    /// <inheritdoc />
    public event EventHandler<IDataChannel>? ChannelOpened;

    /// <inheritdoc />
    public event EventHandler? Failed;

    /// <inheritdoc />
    public string? LocalDescription { get; set; }

    /// <inheritdoc />
    public string? RemoteDescription { get; set; }

    /// <summary>
    /// Gets the offers answered.
    /// </summary>
    public List<string> Offers { get; } = [];

    /// <summary>
    /// Gets the candidates added.
    /// </summary>
    public List<IceCandidate> Candidates { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the connection was closed.
    /// </summary>
    public bool Disposed { get; private set; }

    /// <summary>
    /// Raises <see cref="ChannelOpened"/> for a channel of the other side.
    /// </summary>
    /// <param name="channel">The channel.</param>
    public void Open(IDataChannel channel) => ChannelOpened?.Invoke(this, channel);

    /// <summary>
    /// Raises <see cref="CandidateReady"/>.
    /// </summary>
    /// <param name="candidate">The candidate.</param>
    public void Find(IceCandidate candidate) => CandidateReady?.Invoke(this, candidate);

    /// <summary>
    /// Raises <see cref="Failed"/>.
    /// </summary>
    public void Fail() => Failed?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public Task<IDataChannel> OpenChannelAsync(string label, CancellationToken cancellationToken)
        => Task.FromResult<IDataChannel>(FakeDataChannel.Pair(label).A);

    /// <inheritdoc />
    public Task<string> CreateOfferAsync(CancellationToken cancellationToken) => Task.FromResult(LocalDescription!);

    /// <inheritdoc />
    public Task<string> AnswerAsync(string offer, CancellationToken cancellationToken)
    {
        Offers.Add(offer);
        RemoteDescription = offer;
        return Task.FromResult(LocalDescription!);
    }

    /// <inheritdoc />
    public Task ApplyAnswerAsync(string answer, CancellationToken cancellationToken)
    {
        RemoteDescription = answer;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void AddCandidate(IceCandidate candidate) => Candidates.Add(candidate);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
