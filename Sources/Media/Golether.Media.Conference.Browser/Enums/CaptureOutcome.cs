namespace Golether.Media.Conference.Browser.Enums;

/// <summary>
/// What became of a camera or a microphone the page was asked to capture.
/// </summary>
public enum CaptureOutcome
{
    /// <summary>
    /// The device was not asked for: it is switched off.
    /// </summary>
    Off = 0,

    /// <summary>
    /// The device captures.
    /// </summary>
    Started = 1,

    /// <summary>
    /// The user or the browser refused the permission.
    /// </summary>
    Denied = 2,

    /// <summary>
    /// There is no such device.
    /// </summary>
    Missing = 3,

    /// <summary>
    /// The device is there but could not be started (busy, a fault).
    /// </summary>
    Failed = 4,
}
