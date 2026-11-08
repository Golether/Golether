namespace Golether.Transports.WebRtc;

/// <summary>
/// A candidate of a WebRTC connection: a way the other side may be reached.
/// </summary>
/// <param name="Line">The m-line index.</param>
/// <param name="Candidate">The candidate line, starting with <c>candidate:</c>.</param>
public sealed record IceCandidate(uint Line, string Candidate);
