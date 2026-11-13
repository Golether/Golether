using Golether.Client.Enums;

namespace Golether.Client.Services;

/// <summary>
/// Tells which features of the applications this client offers: all of them, or the fewer ones of the browser.
/// </summary>
public interface IFeatureAccess
{
    /// <summary>
    /// Raised when the available features changed, on any thread.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Tells whether a feature may be used now.
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <returns><see langword="true"/> when it is available.</returns>
    bool IsAvailable(AppOnlyFeature feature);
}
