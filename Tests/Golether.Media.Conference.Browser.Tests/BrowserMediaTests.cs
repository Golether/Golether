using Microsoft.Extensions.Time.Testing;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Media.Conference.Browser.Enums;

namespace Golether.Media.Conference.Browser.Tests;

/// <summary>
/// Tests of what flows over a connection of a browser: the devices, the camera quality, the speaking indicator, the
/// data channel and the loudness, decided by the same policies as in the applications.
/// </summary>
public sealed class BrowserMediaTests : IAsyncDisposable
{
    /// <summary>
    /// A participant.
    /// </summary>
    private static readonly PeerId _bob = PeerId.Parse(new string('b', 64));

    /// <summary>
    /// The source of time.
    /// </summary>
    private readonly FakeTimeProvider _time = new();

    /// <summary>
    /// The browser.
    /// </summary>
    private readonly FakeBrowserRtc _rtc = new();

    /// <summary>
    /// The cameras under test.
    /// </summary>
    private readonly BrowserConferenceMedia _media;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserMediaTests"/> class.
    /// </summary>
    public BrowserMediaTests()
    {
        _media = new BrowserConferenceMedia(_rtc, "stun:203.0.113.7:47900", timeProvider: _time);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _media.DisposeAsync();

    /// <summary>
    /// The capture starts with both devices on, as in the applications.
    /// </summary>
    [Fact]
    public async Task Capture_StartsWithBothDevices()
    {
        await _media.StartCaptureAsync(null, null, CancellationToken.None);

        Assert.Equal([(true, true)], _rtc.DeviceRequests);
        Assert.False(_media.CameraOff);
        Assert.False(_media.MicrophoneMuted);
    }

    /// <summary>
    /// A refused camera leaves the meeting going without it: it shows as off and the failure is reported.
    /// </summary>
    [Fact]
    public async Task RefusedCamera_IsSwitchedOff()
    {
        var failures = 0;
        var changes = 0;
        _media.CameraFailed += (_, _) => failures++;
        _media.MuteChanged += (_, _) => changes++;
        _rtc.CameraOutcome = CaptureOutcome.Denied;

        await _media.StartCaptureAsync(null, null, CancellationToken.None);

        Assert.True(_media.CameraOff);
        Assert.False(_media.MicrophoneMuted);
        Assert.Equal(1, failures);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// Switching a device off or on asks the browser for the new state; the same state again asks nothing.
    /// </summary>
    [Fact]
    public async Task Muting_ChangesTheDevices()
    {
        await _media.StartCaptureAsync(null, null, CancellationToken.None);

        _media.SetMuted(microphoneMuted: true, cameraOff: false);
        _media.SetMuted(microphoneMuted: true, cameraOff: false);
        _media.SetMuted(microphoneMuted: true, cameraOff: true);
        await WaitForAsync(() => _rtc.DeviceRequests.Count == 3);

        Assert.Equal([(true, true), (true, false), (false, false)], _rtc.DeviceRequests);
        Assert.True(_media.MicrophoneMuted);
        Assert.True(_media.CameraOff);
    }

    /// <summary>
    /// Before the capture started, switching devices only remembers the state.
    /// </summary>
    [Fact]
    public void Muting_BeforeTheCapture_OnlyRemembers()
    {
        _media.SetMuted(microphoneMuted: true, cameraOff: true);

        Assert.Empty(_rtc.DeviceRequests);
        Assert.True(_media.MicrophoneMuted);
    }

    /// <summary>
    /// A device that ended in the middle counts as switched off, and the failure is reported once.
    /// </summary>
    [Fact]
    public async Task EndedDevice_IsSwitchedOff()
    {
        var failures = 0;
        _media.MicrophoneFailed += (_, _) => failures++;
        await _media.StartCaptureAsync(null, null, CancellationToken.None);

        _media.OnDeviceEnded(CaptureDeviceKind.Microphone);
        _media.OnDeviceEnded(CaptureDeviceKind.Microphone);

        Assert.True(_media.MicrophoneMuted);
        Assert.False(_media.CameraOff);
        Assert.Equal(1, failures);
    }

    /// <summary>
    /// Two bad samples in a row switch the participant to the economy stream; five good ones switch it back.
    /// </summary>
    [Fact]
    public async Task Quality_FollowsTheLine()
    {
        var changes = new List<VideoQualityChange>();
        _media.VideoQualityChanged += (_, change) => changes.Add(change);
        await ConnectAsync();

        _media.OnStats(_bob, new PeerTraffic(0.1, 0.1, 0, 0, 0, 0));
        Assert.Empty(_rtc.Economy);
        _media.OnStats(_bob, new PeerTraffic(0.1, 0.1, 0, 0, 0, 0));
        Assert.Equal([(_bob, true)], _rtc.Economy);

        for (var i = 0; i < 5; i++)
        {
            _media.OnStats(_bob, new PeerTraffic(0, 0.05, 0, 0, 0, 0));
        }

        Assert.Equal([(_bob, true), (_bob, false)], _rtc.Economy);
        Assert.Equal([new VideoQualityChange(_bob, VideoQuality.Low), new VideoQualityChange(_bob, VideoQuality.High)], changes);
    }

    /// <summary>
    /// Statistics the browser could not give change nothing.
    /// </summary>
    [Fact]
    public async Task UnknownStatistics_ChangeNothing()
    {
        await ConnectAsync();

        for (var i = 0; i < 3; i++)
        {
            _media.OnStats(_bob, new PeerTraffic(double.NaN, double.PositiveInfinity, 0, 0, 0, 0));
        }

        Assert.Empty(_rtc.Economy);
    }

    /// <summary>
    /// A voice above the threshold lights the speaking indicator; silence puts it out after the hold time. Levels of
    /// participants without a connection and of a muted microphone are ignored.
    /// </summary>
    [Fact]
    public async Task Speaking_FollowsTheLevel()
    {
        var changes = new List<SpeakingChange>();
        _media.SpeakingChanged += (_, change) => changes.Add(change);
        await _media.StartCaptureAsync(null, null, CancellationToken.None);
        await ConnectAsync();

        _media.OnLevel(PeerId.Parse(new string('c', 64)), 0.5);
        for (var i = 0; i < 3; i++)
        {
            _media.OnLevel(_bob, 0.5);
            _time.Advance(BrowserConferenceMedia.LevelPeriod);
        }

        Assert.Equal([new SpeakingChange(_bob, true)], changes);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal([new SpeakingChange(_bob, true), new SpeakingChange(_bob, false)], changes);

        _media.SetMuted(microphoneMuted: true, cameraOff: false);
        for (var i = 0; i < 3; i++)
        {
            _media.OnLevel(default, 0.5);
            _time.Advance(BrowserConferenceMedia.LevelPeriod);
        }

        Assert.Equal(2, changes.Count);
    }

    /// <summary>
    /// The data channel carries messages only while it is open on a made connection; a closed one leaves the
    /// participants of the sharing.
    /// </summary>
    [Fact]
    public async Task DataChannel_WorksOnlyWhileOpen()
    {
        var received = new List<byte[]>();
        _media.DataReceived += (_, peer, data) => received.Add(data.ToArray());
        await _media.ConnectAsync(_bob, isOfferer: true, CancellationToken.None);

        _media.OnDataOpened(_bob);
        Assert.False(_media.TrySendData(_bob, [1]));
        Assert.Empty(_media.DataPeers);

        _media.OnConnectionState(_bob, PeerLinkState.Connected);
        Assert.True(_media.TrySendData(_bob, [1, 2]));
        Assert.Equal([_bob], _media.DataPeers);
        _media.OnData(_bob, [3]);
        Assert.Equal([3], Assert.Single(received));

        _rtc.SendAccepted = false;
        Assert.False(_media.TrySendData(_bob, [4]));

        _media.OnDataClosed(_bob);
        Assert.Empty(_media.DataPeers);
        _media.OnData(_bob, [5]);
        Assert.Single(received);
        Assert.Equal([1, 2], Assert.Single(_rtc.Sent).Data);
    }

    /// <summary>
    /// The loudness is clamped, passed on at once and given again to a connection made later.
    /// </summary>
    [Fact]
    public async Task Volume_IsKeptForTheParticipant()
    {
        _media.SetVoiceVolume(_bob, 100);
        await ConnectAsync();

        Assert.Equal([(_bob, PcmGain.MAX_VOLUME), (_bob, PcmGain.MAX_VOLUME)], _rtc.Volumes);
    }

    /// <summary>
    /// Stopping closes everything and forgets the participants; the diagnostics then list nobody.
    /// </summary>
    [Fact]
    public async Task Stop_ClosesEverything()
    {
        await _media.StartCaptureAsync(null, null, CancellationToken.None);
        await ConnectAsync();
        _media.OnStats(_bob, new PeerTraffic(0, 0.1, 10, 20, 30, 40));

        var diagnostics = Assert.Single(_media.GetPeerDiagnostics());
        Assert.Equal(new ConferencePeerDiagnostics(_bob, true, false, VideoQuality.High, 10, 20, 30, 40), diagnostics);

        await _media.StopAsync();

        Assert.Equal(1, _rtc.Stops);
        Assert.Empty(_media.GetPeerDiagnostics());
        Assert.Empty(_media.DataPeers);
    }

    /// <summary>
    /// Opens a made connection with the participant.
    /// </summary>
    /// <returns>A task that completes when the connection is made.</returns>
    private async Task ConnectAsync()
    {
        await _media.ConnectAsync(_bob, isOfferer: true, CancellationToken.None);
        _media.OnConnectionState(_bob, PeerLinkState.Connected);
    }

    /// <summary>
    /// Waits for a condition that work started in the background brings about.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Yield();
        }

        Assert.True(condition());
    }
}
