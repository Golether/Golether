namespace Golether.Browser.Transport;

/// <summary>
/// The data channels of the page by their numbers: the callbacks of <c>rtc.js</c> find their channel here. A message or
/// a closing may come before .NET took the channel; it waits until then.
/// </summary>
internal static class DataChannels
{
    /// <summary>
    /// The channels taken by .NET.
    /// </summary>
    private static readonly Dictionary<int, JsDataChannel> _channels = [];

    /// <summary>
    /// The channels that got messages or closed before .NET took them.
    /// </summary>
    private static readonly Dictionary<int, JsDataChannel> _early = [];

    /// <summary>
    /// Takes a channel of the page, with whatever came for it already.
    /// </summary>
    /// <param name="id">The number of the channel.</param>
    /// <returns>The channel.</returns>
    public static JsDataChannel Open(int id)
    {
        if (!_early.Remove(id, out var channel))
        {
            channel = new JsDataChannel(id);
        }

        if (channel.IsOpen)
        {
            _channels[id] = channel;
        }

        return channel;
    }

    /// <summary>
    /// Passes a message to its channel.
    /// </summary>
    /// <param name="id">The number of the channel.</param>
    /// <param name="data">The bytes.</param>
    public static void Deliver(int id, byte[] data) => Find(id).Deliver(data);

    /// <summary>
    /// Marks a channel closed.
    /// </summary>
    /// <param name="id">The number of the channel.</param>
    public static void Closed(int id)
    {
        var channel = Find(id);
        _channels.Remove(id);
        channel.MarkClosed();
    }

    /// <summary>
    /// Forgets a channel .NET no longer needs.
    /// </summary>
    /// <param name="id">The number of the channel.</param>
    /// <returns><see langword="true"/> when the channel was known.</returns>
    public static bool Forget(int id)
    {
        _early.Remove(id);
        return _channels.Remove(id);
    }

    /// <summary>
    /// Finds a channel, keeping a new one for .NET when it was not taken yet.
    /// </summary>
    /// <param name="id">The number of the channel.</param>
    /// <returns>The channel.</returns>
    private static JsDataChannel Find(int id)
    {
        if (_channels.TryGetValue(id, out var channel) || _early.TryGetValue(id, out channel))
        {
            return channel;
        }

        channel = new JsDataChannel(id);
        _early[id] = channel;
        return channel;
    }
}
