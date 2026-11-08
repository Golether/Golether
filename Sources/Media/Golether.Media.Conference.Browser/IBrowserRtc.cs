using Golether.Core.Identity;

namespace Golether.Media.Conference.Browser;

/// <summary>
/// The WebRTC of the browser as <see cref="BrowserConferenceMedia"/> commands it: the page holds the peer
/// connections, the tracks and the data channels and only carries out what it is told. What the browser reports goes
/// back through the <c>On…</c> methods of <see cref="BrowserConferenceMedia"/>.
/// </summary>
public interface IBrowserRtc
{
    /// <summary>
    /// Makes the local camera and microphone match the wanted state: a device switched off is stopped (its light goes
    /// off), a device switched on is captured and put into every connection.
    /// </summary>
    /// <param name="camera">Whether the camera should capture.</param>
    /// <param name="microphone">Whether the microphone should capture.</param>
    /// <param name="cameraId">The chosen camera, or <see langword="null"/> for the default one.</param>
    /// <param name="microphoneId">The chosen microphone, or <see langword="null"/> for the default one.</param>
    /// <returns>What became of each device.</returns>
    Task<CaptureResult> SetDevicesAsync(bool camera, bool microphone, string? cameraId, string? microphoneId);

    /// <summary>
    /// Lists the cameras and microphones the browser knows of.
    /// </summary>
    /// <returns>The devices; their names may be empty until a permission was given.</returns>
    IReadOnlyList<CaptureDevice> ListDevices();

    /// <summary>
    /// Opens a connection with a participant: one video and one audio transceiver in both directions, VP8 and Opus
    /// only; the offerer also opens the data channel and makes the offer.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="isOfferer">Whether this side makes the offer.</param>
    /// <param name="iceServers">The STUN and TURN servers.</param>
    void Open(PeerId peer, bool isOfferer, IReadOnlyList<BrowserIceServer> iceServers);

    /// <summary>
    /// Applies a description of the other side; for an offer the page then makes and reports the answer.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="kind"><see cref="SignalKinds.OFFER"/> or <see cref="SignalKinds.ANSWER"/>.</param>
    /// <param name="sdp">The checked SDP.</param>
    /// <returns>A task that fails when the browser refused the description.</returns>
    Task ApplyDescriptionAsync(PeerId peer, string kind, string sdp);

    /// <summary>
    /// Adds a candidate of the other side.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="line">The m-line index.</param>
    /// <param name="candidate">The candidate line.</param>
    void AddCandidate(PeerId peer, uint line, string candidate);

    /// <summary>
    /// Closes the connection with a participant and takes its picture and voice off the page.
    /// </summary>
    /// <param name="peer">The participant.</param>
    void Close(PeerId peer);

    /// <summary>
    /// Sends one binary message over the data channel of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="data">The message; the page copies it before returning.</param>
    /// <returns><see langword="false"/> when the channel is not open or holds too much unsent data.</returns>
    bool TrySend(PeerId peer, ReadOnlySpan<byte> data);

    /// <summary>
    /// Switches the camera stream sent to a participant between the economy stream and the full one.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="economy">Whether to send the economy stream.</param>
    void SetEconomy(PeerId peer, bool economy);

    /// <summary>
    /// Sets the loudness of the voice of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="volume">The gain, already clamped.</param>
    void SetVolume(PeerId peer, double volume);

    /// <summary>
    /// Closes every connection and stops the camera and the microphone.
    /// </summary>
    void StopAll();
}
