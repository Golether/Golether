namespace Golether.Client.Enums;

/// <summary>
/// A feature of the applications that the browser version does not offer. The desktop and mobile applications are free
/// and have them all.
/// </summary>
public enum AppOnlyFeature
{
    /// <summary>
    /// Drawing over the video with the pen; the strokes of the others are seen without it.
    /// </summary>
    Drawing = 0,

    /// <summary>
    /// Subtitles: the embedded tracks and files of the user; the sound tracks are free.
    /// </summary>
    Subtitles = 1,

    /// <summary>
    /// A picture of the user's own, shown to the others while the camera is off; the pictures of the others are seen
    /// without it.
    /// </summary>
    Avatar = 2,
}
