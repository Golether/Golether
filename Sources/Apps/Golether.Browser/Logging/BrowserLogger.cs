using Microsoft.Extensions.Logging;

namespace Golether.Browser.Logging;

/// <summary>
/// A logger of one category that writes to the console of the browser.
/// </summary>
internal sealed class BrowserLogger : ILogger
{
    /// <summary>
    /// The category, shortened to the name of the class.
    /// </summary>
    private readonly string _category;

    /// <summary>
    /// The lowest level written.
    /// </summary>
    private readonly LogLevel _minimum;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserLogger"/> class.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <param name="minimum">The lowest level written.</param>
    public BrowserLogger(string category, LogLevel minimum)
    {
        _category = category[(category.LastIndexOf('.') + 1)..];
        _minimum = minimum;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimum && logLevel != LogLevel.None;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var line = $"[{logLevel}] {_category}: {formatter(state, exception)}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        Console.WriteLine(line);
    }
}
