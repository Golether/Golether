namespace Golether.Media.Conference;

/// <summary>
/// How long the cameras of two participants may take to connect and how a failed connection is made again: every
/// implementation follows the same rules, so a browser and an application recover together.
/// </summary>
public static class ConnectionRules
{
    /// <summary>
    /// The label of the data channel for sharing pieces of the film.
    /// </summary>
    public const string DATA_CHANNEL_LABEL = "golether";

    /// <summary>
    /// The longest pause before a failed connection is made again, in seconds.
    /// </summary>
    private const int MAX_RESTART_SECONDS = 30;

    /// <summary>
    /// The first pause before a failed connection is made again, in seconds.
    /// </summary>
    private const int FIRST_RESTART_SECONDS = 3;

    /// <summary>
    /// Gets how long a connection may take to be made and verified before it counts as failed. The path through the
    /// relay of the host may come up only after the first connection gave up looking for it (a meeting through the
    /// rendezvous server takes seconds); a connection made again then finds it.
    /// </summary>
    public static TimeSpan ConnectTimeout { get; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Gets how long a step of the negotiation may take: it takes milliseconds, and one that does not finish at all
    /// must not keep the signaling of the participant waiting forever.
    /// </summary>
    public static TimeSpan NegotiationTimeout { get; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Returns the pause before a failed connection is made again.
    /// </summary>
    /// <param name="attempt">How many connections in a row failed before the one that failed now.</param>
    /// <returns>3 s, doubled with every failure in a row, at most 30 s.</returns>
    public static TimeSpan RestartDelay(int attempt)
        => TimeSpan.FromSeconds(Math.Min(MAX_RESTART_SECONDS, FIRST_RESTART_SECONDS << Math.Clamp(attempt, 0, 4)));
}
