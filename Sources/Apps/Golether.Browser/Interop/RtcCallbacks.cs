using System.Runtime.InteropServices.JavaScript;
using Golether.Browser.Transport;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Transports;
using Golether.Transports.WebRtc;

namespace Golether.Browser.Interop;

/// <summary>
/// What the WebRTC side of the page tells .NET: a channel of a proven participant opened, a message arrived, a
/// channel closed.
/// </summary>
internal static partial class RtcCallbacks
{
    /// <summary>
    /// Hands the host session a stream a participant opened after proving its key.
    /// </summary>
    /// <param name="channel">The number of the channel.</param>
    /// <param name="peer">The identifier the participant proved.</param>
    /// <param name="purpose">The purpose from the label of the channel.</param>
    /// <returns><see langword="false"/> when the stream is refused and the page closes the channel.</returns>
    [JSExport]
    public static bool OnChannel(int channel, string peer, int purpose)
    {
        // A browser carries only the control and the file: the cameras of a browser go in its own connection, so
        // the relay streams of the applications are refused.
        if (!PeerId.TryParse(peer, out var id) || (StreamPurpose)purpose is not (StreamPurpose.Control or StreamPurpose.MediaData))
        {
            return false;
        }

        if (WebRtcListener.Current is not { } listener)
        {
            return false;
        }

        var stream = new DataChannelStream(DataChannels.Open(channel));
        if (listener.Offer(new PeerStream(id, (StreamPurpose)purpose, stream, "WebRTC")))
        {
            return true;
        }

        DataChannels.Forget(channel);
        return false;
    }

    /// <summary>
    /// Delivers a message of a channel.
    /// </summary>
    /// <param name="channel">The number of the channel.</param>
    /// <param name="data">The bytes.</param>
    [JSExport]
    public static void OnData(int channel, [JSMarshalAs<JSType.Array<JSType.Number>>] byte[] data)
        => DataChannels.Deliver(channel, data);

    /// <summary>
    /// Marks a channel closed by the other side or by the network.
    /// </summary>
    /// <param name="channel">The number of the channel.</param>
    [JSExport]
    public static void OnClosed(int channel) => DataChannels.Closed(channel);
}
