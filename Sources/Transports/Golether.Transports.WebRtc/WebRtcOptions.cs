namespace Golether.Transports.WebRtc;

/// <summary>
/// The limits of the WebRTC connections of sessions; the defaults are those of the browser (<c>rtc.js</c>).
/// </summary>
public sealed record WebRtcOptions
{
    /// <summary>
    /// Gets how many channels one connection may open.
    /// </summary>
    public int MaxChannelsPerPeer { get; init; } = 24;

    /// <summary>
    /// Gets how many meetings a host handles at once before they proved a key.
    /// </summary>
    public int MaxPendingMeetings { get; init; } = 8;

    /// <summary>
    /// Gets how long a connection may take to be made.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets how long the other side may take to prove its key once the connection is made.
    /// </summary>
    public TimeSpan ProofTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets how long a connection of a host is kept without a channel of the session before it is closed.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(60);
}
