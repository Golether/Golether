using Golether.Client.Services;

namespace Golether.Browser.Services;

/// <summary>
/// The dialogs the shared view models ask for. An error goes to the main view, which shows it over the page; the files
/// of subtitles and of sound tracks belong to the applications, so nothing is picked.
/// </summary>
internal sealed class BrowserDialogs : ICommonDialogs
{
    /// <summary>
    /// Raised with the title and the text of an error to show.
    /// </summary>
    public event EventHandler<(string Title, string Message)>? ErrorShown;

    /// <inheritdoc />
    public Task ShowErrorAsync(string title, string message)
    {
        ErrorShown?.Invoke(this, (title, message));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> PickSubtitleFileAsync() => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<string?> PickAudioFileAsync() => Task.FromResult<string?>(null);
}
