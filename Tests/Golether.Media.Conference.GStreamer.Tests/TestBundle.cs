using Golether.Components.Catalog;
using Golether.Components.GStreamer;

namespace Golether.Media.Conference.GStreamer.Tests;

/// <summary>
/// Where the tests find GStreamer: the bundle prepared in the repository on Windows, the system elsewhere.
/// </summary>
internal static class TestBundle
{
    /// <summary>
    /// Gets the plugin registry file of the tests.
    /// </summary>
    public static string RegistryFile { get; } = Path.Combine(Path.GetTempPath(), "golether-tests", "gst-registry.bin");

    /// <summary>
    /// Finds the bundle prepared in the repository.
    /// </summary>
    /// <returns>The bundle root, or <see langword="null"/> outside Windows or when it is not prepared.</returns>
    public static string? Find()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Native", ComponentCatalog.CurrentRuntimeIdentifier, "gstreamer");
            if (GStreamerBundle.IsComplete(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Gives the reason to skip a test that needs GStreamer, or <see langword="null"/> when it is there.
    /// </summary>
    /// <param name="bundle">The bundle found.</param>
    /// <returns>The reason, or <see langword="null"/>.</returns>
    public static string? SkipReason(string? bundle)
        => bundle is null && OperatingSystem.IsWindows() ? "The GStreamer bundle is not prepared (run golether-components fetch)." : null;
}
