using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Media.Conference;
using Golether.Media.Conference.Browser;
using Golether.Media.Conference.Browser.Enums;

namespace Golether.Browser.Interop;

/// <summary>
/// The WebRTC of the page behind <see cref="IBrowserRtc"/>: each command goes to <c>conference.js</c> as it is.
/// </summary>
internal sealed class JsBrowserRtc : IBrowserRtc
{
    /// <summary>
    /// The bits of one outcome in the answer of <see cref="ConferenceInterop.SetDevicesAsync"/>.
    /// </summary>
    private const int OUTCOME_MASK = 0xF;

    /// <summary>
    /// Where the outcome of the microphone starts in that answer.
    /// </summary>
    private const int MICROPHONE_SHIFT = 4;

    /// <inheritdoc />
    public async Task<CaptureResult> SetDevicesAsync(bool camera, bool microphone, string? cameraId, string? microphoneId)
    {
        var outcome = await ConferenceInterop.SetDevicesAsync(camera, microphone, cameraId, microphoneId).ConfigureAwait(false);
        return new CaptureResult(Outcome(outcome), Outcome(outcome >> MICROPHONE_SHIFT));
    }

    /// <inheritdoc />
    public IReadOnlyList<CaptureDevice> ListDevices()
    {
        var devices = new List<CaptureDevice>();
        foreach (var entry in ConferenceInterop.Devices())
        {
            var parts = entry.Split('|', 3);
            if (parts.Length == 3 && parts[1].Length > 0 && parts[0] is "0" or "1")
            {
                devices.Add(new CaptureDevice(parts[1], parts[2], parts[0] == "0" ? CaptureDeviceKind.Camera : CaptureDeviceKind.Microphone));
            }
        }

        return devices;
    }

    /// <inheritdoc />
    public void Open(PeerId peer, bool isOfferer, IReadOnlyList<BrowserIceServer> iceServers)
        => ConferenceInterop.Open(peer.Value, isOfferer, WriteServers(iceServers));

    /// <inheritdoc />
    public Task ApplyDescriptionAsync(PeerId peer, string kind, string sdp)
        => ConferenceInterop.ApplyDescriptionAsync(peer.Value, kind, sdp);

    /// <inheritdoc />
    public void AddCandidate(PeerId peer, uint line, string candidate)
        => ConferenceInterop.AddCandidate(peer.Value, (int)Math.Min(line, int.MaxValue), candidate);

    /// <inheritdoc />
    public void Close(PeerId peer) => ConferenceInterop.Close(peer.Value);

    /// <inheritdoc />
    public bool TrySend(PeerId peer, ReadOnlySpan<byte> data)
    {
        // The interop takes a writable span; the page only copies the bytes out of it, so nothing is written.
        var view = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(data), data.Length);
        return ConferenceInterop.Send(peer.Value, view);
    }

    /// <inheritdoc />
    public void SetEconomy(PeerId peer, bool economy) => ConferenceInterop.SetEconomy(peer.Value, economy);

    /// <inheritdoc />
    public void SetVolume(PeerId peer, double volume) => ConferenceInterop.SetVolume(peer.Value, volume);

    /// <inheritdoc />
    public void StopAll() => ConferenceInterop.StopAll();

    /// <summary>
    /// Writes the servers as the page reads them: <c>[{ "url", "username", "credential" }]</c>.
    /// </summary>
    /// <param name="servers">The servers.</param>
    /// <returns>The JSON.</returns>
    private static string WriteServers(IReadOnlyList<BrowserIceServer> servers)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            foreach (var server in servers)
            {
                json.WriteStartObject();
                json.WriteString("url", server.Url);
                if (server.Username is { } username && server.Credential is { } credential)
                {
                    json.WriteString("username", username);
                    json.WriteString("credential", credential);
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Reads one outcome of the answer of the page.
    /// </summary>
    /// <param name="bits">The answer shifted to the outcome.</param>
    /// <returns>The outcome; an unknown value counts as a failure.</returns>
    private static CaptureOutcome Outcome(int bits)
    {
        var value = (CaptureOutcome)(bits & OUTCOME_MASK);
        return Enum.IsDefined(value) ? value : CaptureOutcome.Failed;
    }
}
