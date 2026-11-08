using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Media.Conference.Browser.Enums;

namespace Golether.Media.Conference.Browser.Tests;

/// <summary>
/// A browser that does what it is told and writes it down, so the tests can see the decisions of
/// <see cref="BrowserConferenceMedia"/>.
/// </summary>
internal sealed class FakeBrowserRtc : IBrowserRtc
{
    /// <summary>
    /// Gets the connections opened, in order: participant and whether this side offers.
    /// </summary>
    public List<(PeerId Peer, bool IsOfferer, IReadOnlyList<BrowserIceServer> IceServers)> Opened { get; } = [];

    /// <summary>
    /// Gets the connections closed, in order.
    /// </summary>
    public List<PeerId> Closed { get; } = [];

    /// <summary>
    /// Gets the descriptions applied, in order.
    /// </summary>
    public List<(PeerId Peer, string Kind)> Descriptions { get; } = [];

    /// <summary>
    /// Gets the candidates added, in order.
    /// </summary>
    public List<(PeerId Peer, uint Line, string Candidate)> Candidates { get; } = [];

    /// <summary>
    /// Gets the requests for devices, in order: camera and microphone wanted.
    /// </summary>
    public List<(bool Camera, bool Microphone)> DeviceRequests { get; } = [];

    /// <summary>
    /// Gets the changes of the camera stream, in order.
    /// </summary>
    public List<(PeerId Peer, bool Economy)> Economy { get; } = [];

    /// <summary>
    /// Gets the loudness set, in order.
    /// </summary>
    public List<(PeerId Peer, double Volume)> Volumes { get; } = [];

    /// <summary>
    /// Gets the messages sent over data channels.
    /// </summary>
    public List<(PeerId Peer, byte[] Data)> Sent { get; } = [];

    /// <summary>
    /// Gets or sets what the camera does when asked for.
    /// </summary>
    public CaptureOutcome CameraOutcome { get; set; } = CaptureOutcome.Started;

    /// <summary>
    /// Gets or sets what the microphone does when asked for.
    /// </summary>
    public CaptureOutcome MicrophoneOutcome { get; set; } = CaptureOutcome.Started;

    /// <summary>
    /// Gets or sets a value indicating whether a data channel takes a message.
    /// </summary>
    public bool SendAccepted { get; set; } = true;

    /// <summary>
    /// Gets or sets the failure of the next description, or <see langword="null"/>.
    /// </summary>
    public Exception? DescriptionFailure { get; set; }

    /// <summary>
    /// Gets how many times everything was stopped.
    /// </summary>
    public int Stops { get; private set; }

    /// <inheritdoc />
    public Task<CaptureResult> SetDevicesAsync(bool camera, bool microphone, string? cameraId, string? microphoneId)
    {
        DeviceRequests.Add((camera, microphone));
        return Task.FromResult(new CaptureResult(camera ? CameraOutcome : CaptureOutcome.Off, microphone ? MicrophoneOutcome : CaptureOutcome.Off));
    }

    /// <inheritdoc />
    public IReadOnlyList<CaptureDevice> ListDevices() => [new CaptureDevice("cam", "Camera", CaptureDeviceKind.Camera)];

    /// <inheritdoc />
    public void Open(PeerId peer, bool isOfferer, IReadOnlyList<BrowserIceServer> iceServers) => Opened.Add((peer, isOfferer, iceServers));

    /// <inheritdoc />
    public Task ApplyDescriptionAsync(PeerId peer, string kind, string sdp)
    {
        Descriptions.Add((peer, kind));
        var failure = DescriptionFailure;
        DescriptionFailure = null;
        return failure is null ? Task.CompletedTask : Task.FromException(failure);
    }

    /// <inheritdoc />
    public void AddCandidate(PeerId peer, uint line, string candidate) => Candidates.Add((peer, line, candidate));

    /// <inheritdoc />
    public void Close(PeerId peer) => Closed.Add(peer);

    /// <inheritdoc />
    public bool TrySend(PeerId peer, ReadOnlySpan<byte> data)
    {
        if (!SendAccepted)
        {
            return false;
        }

        Sent.Add((peer, data.ToArray()));
        return true;
    }

    /// <inheritdoc />
    public void SetEconomy(PeerId peer, bool economy) => Economy.Add((peer, economy));

    /// <inheritdoc />
    public void SetVolume(PeerId peer, double volume) => Volumes.Add((peer, volume));

    /// <inheritdoc />
    public void StopAll() => Stops++;
}
