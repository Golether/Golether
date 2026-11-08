namespace Golether.Transports.WebRtc;

/// <summary>
/// A description of a WebRTC connection as a browser writes it (<c>RTCSessionDescriptionInit</c>).
/// </summary>
/// <param name="Type"><c>offer</c> or <c>answer</c>.</param>
/// <param name="Sdp">The SDP.</param>
public sealed record SignalDescription(string? Type, string? Sdp);
