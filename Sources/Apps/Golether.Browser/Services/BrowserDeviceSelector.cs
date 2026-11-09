using Golether.Client.ViewModels;
using Golether.Media.Conference;
using Golether.Media.Conference.Browser;

namespace Golether.Browser.Services;

/// <summary>
/// The cameras and microphones of the browser for the device choice in the settings.
/// </summary>
internal sealed class BrowserDeviceSelector : ICaptureDeviceSelector
{
    /// <summary>
    /// The cameras of the page.
    /// </summary>
    private readonly BrowserConferenceMedia _conference;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserDeviceSelector"/> class.
    /// </summary>
    /// <param name="conference">The cameras of the page.</param>
    public BrowserDeviceSelector(BrowserConferenceMedia conference)
    {
        _conference = conference ?? throw new ArgumentNullException(nameof(conference));
    }

    /// <inheritdoc />
    public bool IsAvailable => _conference.IsAvailable;

    /// <inheritdoc />
    public IReadOnlyList<CaptureDevice> GetDevices() => _conference.GetDevices();

    /// <inheritdoc />
    public Task SelectDevicesAsync(CaptureDevice? camera, CaptureDevice? microphone, CancellationToken cancellationToken)
        => _conference.StartCaptureAsync(camera, microphone, cancellationToken);
}
