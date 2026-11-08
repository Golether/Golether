using System.Runtime.InteropServices.JavaScript;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Media.Conference.Browser;
using Golether.Media.Conference.Browser.Enums;

namespace Golether.Browser.Interop;

/// <summary>
/// What <c>conference.js</c> reports about the cameras, the voices and the data channels; everything is passed to
/// <see cref="BrowserConferenceMedia"/>, which decides. A participant named by an identifier that does not parse is
/// ignored.
/// </summary>
internal static partial class ConferenceCallbacks
{
    /// <summary>
    /// The page made a description or a candidate for a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="kind">The signaling kind.</param>
    /// <param name="payload">The payload.</param>
    [JSExport]
    public static void OnSignal(string peer, string kind, string payload)
    {
        if (Find(peer) is { } id)
        {
            Media?.OnSignal(id, kind, payload);
        }
    }

    /// <summary>
    /// The state of a connection changed.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="state">The <see cref="PeerLinkState"/>.</param>
    [JSExport]
    public static void OnState(string peer, int state)
    {
        if (Find(peer) is { } id && Enum.IsDefined((PeerLinkState)state))
        {
            Media?.OnConnectionState(id, (PeerLinkState)state);
        }
    }

    /// <summary>
    /// The data channel of a participant opened.
    /// </summary>
    /// <param name="peer">The participant.</param>
    [JSExport]
    public static void OnDataOpened(string peer)
    {
        if (Find(peer) is { } id)
        {
            Media?.OnDataOpened(id);
        }
    }

    /// <summary>
    /// The data channel of a participant closed.
    /// </summary>
    /// <param name="peer">The participant.</param>
    [JSExport]
    public static void OnDataClosed(string peer)
    {
        if (Find(peer) is { } id)
        {
            Media?.OnDataClosed(id);
        }
    }

    /// <summary>
    /// A message arrived over the data channel of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="data">The message.</param>
    [JSExport]
    public static void OnData(string peer, [JSMarshalAs<JSType.Array<JSType.Number>>] byte[] data)
    {
        if (Find(peer) is { } id)
        {
            Media?.OnData(id, data);
        }
    }

    /// <summary>
    /// The statistics of a connection, once a second; a value the browser could not give is not a number.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="fractionLost">The share of the video sent to the participant that was lost.</param>
    /// <param name="roundTrip">The round trip in seconds.</param>
    /// <param name="videoSent">Camera frames sent.</param>
    /// <param name="videoReceived">Camera frames received.</param>
    /// <param name="audioSent">Voice packets sent.</param>
    /// <param name="audioReceived">Voice packets received.</param>
    [JSExport]
    public static void OnStats(string peer, double fractionLost, double roundTrip, double videoSent, double videoReceived, double audioSent, double audioReceived)
    {
        if (Find(peer) is { } id)
        {
            Media?.OnStats(id, new PeerTraffic(fractionLost, roundTrip, Count(videoSent), Count(videoReceived), Count(audioSent), Count(audioReceived)));
        }
    }

    /// <summary>
    /// The level of a voice, ten times a second.
    /// </summary>
    /// <param name="peer">The participant, or an empty string for this device.</param>
    /// <param name="level">The level: 0 is silence, 1 is full scale.</param>
    [JSExport]
    public static void OnLevel(string peer, double level)
    {
        if (peer.Length == 0)
        {
            Media?.OnLevel(default, level);
        }
        else if (Find(peer) is { } id)
        {
            Media?.OnLevel(id, level);
        }
    }

    /// <summary>
    /// A device stopped in the middle of the meeting.
    /// </summary>
    /// <param name="kind">The <see cref="CaptureDeviceKind"/>.</param>
    [JSExport]
    public static void OnDeviceEnded(int kind)
    {
        if (Enum.IsDefined((CaptureDeviceKind)kind))
        {
            Media?.OnDeviceEnded((CaptureDeviceKind)kind);
        }
    }

    /// <summary>
    /// Gets the cameras of the page, or <see langword="null"/> before it started.
    /// </summary>
    private static BrowserConferenceMedia? Media => BrowserApp.Instance?.Conference;

    /// <summary>
    /// Reads an identifier of a participant the page passed.
    /// </summary>
    /// <param name="peer">The identifier.</param>
    /// <returns>The participant, or <see langword="null"/> when it does not parse.</returns>
    private static PeerId? Find(string peer) => PeerId.TryParse(peer, out var id) ? id : null;

    /// <summary>
    /// Turns a counter of the page into a whole number.
    /// </summary>
    /// <param name="value">The counter.</param>
    /// <returns>The counter, 0 when the browser could not give it.</returns>
    private static long Count(double value) => double.IsFinite(value) && value > 0 ? (long)Math.Min(value, long.MaxValue) : 0;
}
