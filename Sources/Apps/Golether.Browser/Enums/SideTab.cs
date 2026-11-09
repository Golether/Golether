namespace Golether.Browser.Enums;

/// <summary>
/// The area under the microphone and the camera in the participants column, as on the desktop.
/// </summary>
internal enum SideTab
{
    /// <summary>
    /// Nothing is open: the participants take the whole column.
    /// </summary>
    None = 0,

    /// <summary>
    /// The chat.
    /// </summary>
    Chat = 1,

    /// <summary>
    /// The event feed.
    /// </summary>
    Events = 2,
}
