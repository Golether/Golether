using Microsoft.Extensions.Logging;

namespace Golether.Browser.Logging;

/// <summary>
/// Writes the log to the console of the browser. The console logger of .NET needs a thread of its own, which a page
/// does not have.
/// </summary>
internal sealed class BrowserLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// The lowest level written.
    /// </summary>
    private readonly LogLevel _minimum;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserLoggerProvider"/> class.
    /// </summary>
    /// <param name="minimum">The lowest level written.</param>
    public BrowserLoggerProvider(LogLevel minimum)
    {
        _minimum = minimum;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new BrowserLogger(categoryName, _minimum);

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
