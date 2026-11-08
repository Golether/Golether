namespace Golether.Transports.WebRtc;

/// <summary>
/// A candidate as a browser writes it (<c>RTCIceCandidateInit</c>); the other fields a browser adds are skipped.
/// </summary>
/// <param name="Candidate">The candidate line.</param>
/// <param name="SdpMLineIndex">The m-line index, or <see langword="null"/> when the browser gave none.</param>
public sealed record SignalCandidate(string? Candidate, uint? SdpMLineIndex);
