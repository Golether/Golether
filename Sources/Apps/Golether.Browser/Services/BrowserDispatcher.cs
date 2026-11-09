using Avalonia.Threading;
using Golether.Client.Services;

namespace Golether.Browser.Services;

/// <summary>
/// Runs the work of the view models on the thread of Avalonia.
/// </summary>
internal sealed class BrowserDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Dispatcher.UIThread.Post(action);
    }
}
