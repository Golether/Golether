using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// The WebRTC side of the page (<c>rtc.js</c>): the signalling WebSocket, the peer connections, the proof of the
/// device keys and the data channels that carry the streams of a session.
/// </summary>
internal static partial class RtcInterop
{
    /// <summary>
    /// Takes a room on the rendezvous server and starts accepting participants. Every data channel of an admitted
    /// connection is handed to <see cref="RtcCallbacks.OnChannel"/>.
    /// </summary>
    /// <param name="signalUrl">The WebSocket of the server.</param>
    /// <param name="stunUrl">The STUN server.</param>
    /// <param name="room">The room.</param>
    /// <returns>A task that completes when the room is taken; it fails with the name of the refusal.</returns>
    [JSImport("rtc.hostOpen", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    public static partial Task HostOpenAsync(string signalUrl, string stunUrl, string room);

    /// <summary>
    /// Releases the room and closes every connection with participants.
    /// </summary>
    [JSImport("rtc.hostClose", JsModule.NAME)]
    public static partial void HostClose();

    /// <summary>
    /// Opens a data channel to the host of a room: through a connection already proven, or through a new meeting.
    /// </summary>
    /// <param name="signalUrl">The WebSocket of the server.</param>
    /// <param name="stunUrl">The STUN server.</param>
    /// <param name="room">The room.</param>
    /// <param name="expectedPeer">The identifier of the host from the invitation.</param>
    /// <param name="purpose">The purpose of the stream.</param>
    /// <returns>The number of the channel; the task fails with the reason when the host was not reached or
    /// presented another key.</returns>
    [JSImport("rtc.connect", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    public static partial Task<int> ConnectAsync(string signalUrl, string stunUrl, string room, string expectedPeer, int purpose);

    /// <summary>
    /// Sends one message over a data channel; the page copies the bytes before returning.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="data">The bytes, at most <see cref="Transports.WebRtc.DataChannelStream.MAX_MESSAGE"/>.</param>
    /// <returns><see langword="true"/> when the channel holds so much unsent data that the next send should wait for
    /// <see cref="DrainAsync"/>.</returns>
    [JSImport("rtc.send", JsModule.NAME)]
    public static partial bool Send(int channel, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    /// <summary>
    /// Waits until the channel has sent most of what it was given.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>A task that completes when more may be sent.</returns>
    [JSImport("rtc.drain", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    public static partial Task DrainAsync(int channel);

    /// <summary>
    /// Closes a data channel.
    /// </summary>
    /// <param name="channel">The channel.</param>
    [JSImport("rtc.close", JsModule.NAME)]
    public static partial void Close(int channel);
}
