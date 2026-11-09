using Golether.Client.Enums;
using Golether.Client.Services;

namespace Golether.Browser.Services;

/// <summary>
/// The paid features in a browser, as in the free mobile client: the strokes of others are seen, but drawing, an own
/// avatar and subtitles belong to the applications.
/// </summary>
internal sealed class BrowserFeatures : IFeatureAccess
{
    /// <summary>
    /// Never raised: nothing can be bought in a browser.
    /// </summary>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public bool IsAvailable(PaidFeature feature) => false;
}
