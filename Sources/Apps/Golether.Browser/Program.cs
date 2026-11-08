namespace Golether.Browser;

/// <summary>
/// The entry point of the WebAssembly part of the page. The page starts .NET and then works through the exports of
/// <see cref="Interop.BrowserApi"/>; there is nothing to do before that.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs when the page started .NET.
    /// </summary>
    public static void Main()
    {
    }
}
