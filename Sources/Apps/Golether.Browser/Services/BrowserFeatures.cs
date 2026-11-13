using Golether.Client.Enums;
using Golether.Client.Services;

namespace Golether.Browser.Services;

/// <summary>
/// The features in a browser, which lacks some of the applications: the strokes of others are seen, but drawing, an own
/// avatar and subtitles belong to the applications.
/// </summary>
internal sealed class BrowserFeatures : IFeatureAccess
{
    /// <summary>
    /// Never raised: the browser version always lacks the same features.
    /// </summary>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public bool IsAvailable(AppOnlyFeature feature) => false;
}
