namespace Golether.UI.Enums;

/// <summary>
/// What is known about a newer version of the application.
/// </summary>
public enum UpdateStatus
{
    /// <summary>
    /// Nobody has asked yet.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The source is being asked now.
    /// </summary>
    Checking = 1,

    /// <summary>
    /// This is the newest version.
    /// </summary>
    Latest = 2,

    /// <summary>
    /// A newer version is out.
    /// </summary>
    Available = 3,

    /// <summary>
    /// The source could not be asked.
    /// </summary>
    Failed = 4,
}
