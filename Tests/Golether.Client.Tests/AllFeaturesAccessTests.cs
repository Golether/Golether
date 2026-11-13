using Golether.Client.Enums;
using Golether.Client.Services;

namespace Golether.Client.Tests;

/// <summary>
/// Tests of <see cref="AllFeaturesAccess"/>: the free applications have every feature.
/// </summary>
public sealed class AllFeaturesAccessTests
{
    /// <summary>
    /// Every feature of the applications is available.
    /// </summary>
    [Fact]
    public void EverythingIsAvailable()
    {
        var access = new AllFeaturesAccess();

        Assert.All(Enum.GetValues<AppOnlyFeature>(), feature => Assert.True(access.IsAvailable(feature)));
    }
}
