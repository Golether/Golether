using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// The cameras, the voices and the data channels of the page (<c>conference.js</c>), as .NET commands them.
/// </summary>
internal static partial class ConferenceInterop
{
    /// <summary>
    /// Makes the camera and the microphone match the wanted state.
    /// </summary>
    /// <param name="camera">Whether the camera should capture.</param>
    /// <param name="microphone">Whether the microphone should capture.</param>
    /// <param name="cameraId">The chosen camera, or <see langword="null"/>.</param>
    /// <param name="microphoneId">The chosen microphone, or <see langword="null"/>.</param>
    /// <returns>The outcome of the camera in the low four bits, of the microphone in the next four.</returns>
    [JSImport("conference.setDevices", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    public static partial Task<int> SetDevicesAsync(bool camera, bool microphone, string? cameraId, string? microphoneId);

    /// <summary>
    /// Lists the devices the page knows of.
    /// </summary>
    /// <returns>Every device as <c>kind|id|name</c>, the kind being 0 for a camera and 1 for a microphone.</returns>
    [JSImport("conference.devices", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Array<JSType.String>>]
    public static partial string[] Devices();

    /// <summary>
    /// Opens a connection with a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="isOfferer">Whether this side makes the offer.</param>
    /// <param name="iceServers">The STUN and TURN servers as JSON: <c>[{ "url", "username", "credential" }]</c>.</param>
    [JSImport("conference.open", JsModule.NAME)]
    public static partial void Open(string peer, bool isOfferer, string iceServers);

    /// <summary>
    /// Applies a description of the other side.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="kind"><c>offer</c> or <c>answer</c>.</param>
    /// <param name="sdp">The SDP.</param>
    /// <returns>A task that fails when the browser refused the description.</returns>
    [JSImport("conference.applyDescription", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    public static partial Task ApplyDescriptionAsync(string peer, string kind, string sdp);

    /// <summary>
    /// Adds a candidate of the other side.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="line">The m-line index.</param>
    /// <param name="candidate">The candidate line.</param>
    [JSImport("conference.addCandidate", JsModule.NAME)]
    public static partial void AddCandidate(string peer, int line, string candidate);

    /// <summary>
    /// Closes the connection with a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    [JSImport("conference.close", JsModule.NAME)]
    public static partial void Close(string peer);

    /// <summary>
    /// Sends a message over the data channel of a participant; the page copies the bytes before returning.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="data">The message.</param>
    /// <returns><see langword="false"/> when the channel is not open or holds too much.</returns>
    [JSImport("conference.send", JsModule.NAME)]
    public static partial bool Send(string peer, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    /// <summary>
    /// Switches the camera stream sent to a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="economy">Whether to send the economy stream.</param>
    [JSImport("conference.setEconomy", JsModule.NAME)]
    public static partial void SetEconomy(string peer, bool economy);

    /// <summary>
    /// Sets the loudness of the voice of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="volume">The gain.</param>
    [JSImport("conference.setVolume", JsModule.NAME)]
    public static partial void SetVolume(string peer, double volume);

    /// <summary>
    /// Closes every connection and stops the devices.
    /// </summary>
    [JSImport("conference.stopAll", JsModule.NAME)]
    public static partial void StopAll();

    /// <summary>
    /// Asks for the devices and wakes the sound while the browser still counts the click as the user's: a phone
    /// gives the camera, the microphone and the sound only then. The next change of the devices takes them over.
    /// </summary>
    /// <param name="camera">Whether to ask for the camera.</param>
    /// <param name="microphone">Whether to ask for the microphone.</param>
    [JSImport("conference.prepare", JsModule.NAME)]
    public static partial void Prepare(bool camera, bool microphone);
}
