using Golether.Core.Identity;

namespace Golether.Media.Conference.Browser;

/// <summary>
/// One connection of the cameras with a participant as <see cref="BrowserConferenceMedia"/> keeps it: its role, its
/// state as the browser reported it, its timers and its camera quality. A connection made again is a new object, so a
/// timer of the old one finds itself replaced and does nothing.
/// </summary>
internal sealed class BrowserPeer : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserPeer"/> class.
    /// </summary>
    /// <param name="id">The participant.</param>
    /// <param name="isOfferer">Whether this side makes the offer.</param>
    /// <param name="attempt">How many connections in a row failed before this one.</param>
    public BrowserPeer(PeerId id, bool isOfferer, int attempt)
    {
        Id = id;
        IsOfferer = isOfferer;
        Attempt = attempt;
    }

    /// <summary>
    /// Gets the participant.
    /// </summary>
    public PeerId Id { get; }

    /// <summary>
    /// Gets a value indicating whether this side makes the offer.
    /// </summary>
    public bool IsOfferer { get; }

    /// <summary>
    /// Gets how many connections in a row failed before this one.
    /// </summary>
    public int Attempt { get; }

    /// <summary>
    /// Gets or sets a value indicating whether a path is in use and the certificate of the other side matched.
    /// </summary>
    public bool Connected { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the connection was ever made: a later failure then starts the count of
    /// attempts again.
    /// </summary>
    public bool WasConnected { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the data channel is open.
    /// </summary>
    public bool DataOpen { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a failure was reported and not cleared, so that it is handled once.
    /// </summary>
    public bool Failing { get; set; }

    /// <summary>
    /// Gets or sets the session identifier of the first offer of the other side; a later offer with another one means
    /// the other side made a new connection.
    /// </summary>
    public string? RemoteSession { get; set; }

    /// <summary>
    /// Gets the choice of the camera stream this participant gets.
    /// </summary>
    public VideoQualityPolicy Quality { get; } = new();

    /// <summary>
    /// Gets or sets the last statistics of the connection.
    /// </summary>
    public PeerTraffic Traffic { get; set; }

    /// <summary>
    /// Gets or sets the timer that fails a connection not made in time.
    /// </summary>
    public ITimer? ConnectTimer { get; set; }

    /// <summary>
    /// Gets or sets the timer that makes a failed connection again.
    /// </summary>
    public ITimer? RestartTimer { get; set; }

    /// <summary>
    /// Stops the timers of the connection.
    /// </summary>
    public void Dispose()
    {
        ConnectTimer?.Dispose();
        RestartTimer?.Dispose();
    }
}
