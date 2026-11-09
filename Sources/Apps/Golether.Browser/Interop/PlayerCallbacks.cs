using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// What the film element of the page reports (<c>player.js</c>).
/// </summary>
internal static partial class PlayerCallbacks
{
    /// <summary>
    /// The sound tracks of the film became known or another one plays now.
    /// </summary>
    [JSExport]
    public static void TracksChanged() => BrowserApp.Instance?.LocalControls.OnTracksChanged();
}
