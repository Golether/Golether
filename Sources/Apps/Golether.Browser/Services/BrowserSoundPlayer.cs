using Golether.Browser.Interop;
using Golether.Client.Services;

namespace Golether.Browser.Services;

/// <summary>
/// Plays the short sounds of the application through the Web Audio of the page.
/// </summary>
internal sealed class BrowserSoundPlayer : ISoundPlayer
{
    /// <inheritdoc />
    public void Play(byte[] wave)
    {
        ArgumentNullException.ThrowIfNull(wave);

        PageInterop.PlaySound(wave);
    }
}
