namespace Golether.Transports.WebRtc;

/// <summary>
/// Waits for the proof of the other side in the channel of the proofs.
/// </summary>
internal static class ProofExchange
{
    /// <summary>
    /// Waits for the next text message of a channel.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="timeProvider">The source of time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text.</returns>
    /// <exception cref="IOException">Nothing came in time, or the channel closed.</exception>
    public static async Task<string> ReadTextAsync(IDataChannel channel, TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var text = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<DataChannelMessage> received = (_, message) =>
        {
            if (message.Text is { } value)
            {
                text.TrySetResult(value);
            }
        };
        EventHandler closed = (_, _) => text.TrySetException(new IOException("The channel of the proofs closed."));

        channel.MessageReceived += received;
        channel.Closed += closed;
        try
        {
            if (!channel.IsOpen)
            {
                throw new IOException("The channel of the proofs closed.");
            }

            return await text.Task.WaitAsync(timeout, timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new IOException("The other side did not prove its key in time.", ex);
        }
        finally
        {
            channel.MessageReceived -= received;
            channel.Closed -= closed;
        }
    }
}
