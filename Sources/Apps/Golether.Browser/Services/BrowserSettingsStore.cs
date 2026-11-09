using Golether.Browser.Interop;
using Golether.Core.Data.Stores;

namespace Golether.Browser.Services;

/// <summary>
/// The settings of the browser in its local storage, under the keys the applications keep in their database: the
/// language, the volume, the sound track, the cameras.
/// </summary>
internal sealed class BrowserSettingsStore : ISettingsStore
{
    /// <summary>
    /// The beginning of every key in the local storage, so the settings do not mix with anything else of the site.
    /// </summary>
    private const string PREFIX = "golether.";

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken) => Task.FromResult(Get(key));

    /// <inheritdoc />
    public Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        Set(key, value);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads a setting; the local storage answers at once.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return PageInterop.GetSetting(PREFIX + key);
    }

    /// <summary>
    /// Stores a setting.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        PageInterop.SetSetting(PREFIX + key, value);
    }
}
