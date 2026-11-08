namespace Golether.Media.Conference.Browser;

/// <summary>
/// The statistics of the connection with one participant the page reports once a second.
/// </summary>
/// <param name="FractionLost">The share of the video sent to the participant that was lost (0–1), or
/// <see langword="null"/> while unknown.</param>
/// <param name="RoundTrip">The round trip in seconds, or <see langword="null"/> while unknown.</param>
/// <param name="VideoSent">Camera frames sent to the participant.</param>
/// <param name="VideoReceived">Camera frames received from the participant.</param>
/// <param name="AudioSent">Voice packets sent to the participant.</param>
/// <param name="AudioReceived">Voice packets received from the participant.</param>
public readonly record struct PeerTraffic(
    double? FractionLost,
    double? RoundTrip,
    long VideoSent,
    long VideoReceived,
    long AudioSent,
    long AudioReceived);
