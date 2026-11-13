using Golether.Client.Enums;
using Golether.Client.Services;

namespace Golether.Client.Tests;

/// <summary>
/// Features of the applications a test opens and closes.
/// </summary>
internal sealed class SwitchableFeatures : IFeatureAccess
{
    /// <summary>
    /// The features available now.
    /// </summary>
    private readonly HashSet<AppOnlyFeature> _available = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SwitchableFeatures"/> class.
    /// </summary>
    /// <param name="available">The features available from the start.</param>
    public SwitchableFeatures(params AppOnlyFeature[] available)
    {
        _available.UnionWith(available);
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool IsAvailable(AppOnlyFeature feature) => _available.Contains(feature);

    /// <summary>
    /// Opens or closes a feature and tells the listeners.
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <param name="available">Whether it becomes available.</param>
    public void Set(AppOnlyFeature feature, bool available)
    {
        if (available)
        {
            _available.Add(feature);
        }
        else
        {
            _available.Remove(feature);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
