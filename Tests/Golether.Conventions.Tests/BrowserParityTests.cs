using System.Text.RegularExpressions;

namespace Golether.Conventions.Tests;

/// <summary>
/// The browser version keeps the screen of the desktop application: every control of the main window, known by the
/// name it gives to screen readers, is on the page too, unless it is one of the gaps listed here with the reason.
/// A control added to the desktop window then either reaches the browser or is listed, never forgotten.
/// </summary>
public sealed partial class BrowserParityTests
{
    /// <summary>
    /// The controls of the desktop window the browser does not have, and why.
    /// </summary>
    private static readonly Dictionary<string, string> _gaps = new(StringComparer.Ordinal)
    {
        ["Gallery.Alone.Chip"] = "the gallery of pictures and folders is deferred",
        ["Gallery.Alone.Fold"] = "the gallery of pictures and folders is deferred",
        ["Gallery.BackToFilm"] = "the gallery of pictures and folders is deferred",
        ["Gallery.Grid"] = "the gallery of pictures and folders is deferred",
        ["Gallery.Next"] = "the gallery of pictures and folders is deferred",
        ["Gallery.Previous"] = "the gallery of pictures and folders is deferred",
        ["Main.Host.OpenPortName"] = "a browser always goes through the server of the project",
        ["Main.Host.Port"] = "a browser always goes through the server of the project",
        ["Main.Host.RendezvousName"] = "a browser always goes through the server of the project",
        ["Main.Layout.SplitterName"] = "the adjustable layout is a setting of the desktop",
        ["Main.Panel.Name"] = "the tabs of the participants column fold by themselves",
        ["Main.Resume.ContinueName"] = "continuing where the film stopped is deferred",
        ["Main.Resume.HideName"] = "continuing where the film stopped is deferred",
        ["Main.Update.HideName"] = "a page is always the latest version",
    };

    /// <summary>
    /// Every named control of the desktop main window is on the page, or is a listed gap.
    /// </summary>
    [Fact]
    public void Page_HasTheControlsOfTheDesktopWindow()
    {
        var desktop = NamesIn("Sources/Apps/Golether.UI/Views/MainWindow.axaml");
        var page = NamesIn("Sources/Apps/Golether.Browser/Views/MainView.axaml");

        var missing = desktop.Where(n => !page.Contains(n) && !_gaps.ContainsKey(n)).Select(n => n + " (MainWindow.axaml)");

        ConventionAssert.None(missing, "The browser page lacks controls of the desktop main window; add them or list them as gaps with the reason:");
    }

    /// <summary>
    /// A listed gap is still a control of the desktop window: once the desktop drops it or the page gets it, the list
    /// shrinks.
    /// </summary>
    [Fact]
    public void Gaps_AreStillGaps()
    {
        var desktop = NamesIn("Sources/Apps/Golether.UI/Views/MainWindow.axaml");
        var page = NamesIn("Sources/Apps/Golether.Browser/Views/MainView.axaml");

        var stale = _gaps.Keys.Where(n => !desktop.Contains(n) || page.Contains(n));

        ConventionAssert.None(stale, "These gaps of the browser page are gone; remove them from the list:");
    }

    /// <summary>
    /// Reads the localized names of the controls of a view.
    /// </summary>
    /// <param name="path">The view, relative to the repository.</param>
    /// <returns>The keys of the names.</returns>
    private static HashSet<string> NamesIn(string path)
        => [.. NameKey().Matches(File.ReadAllText(Path.Combine(RepositorySources.Root, path))).Select(m => m.Groups[1].Value)];

    /// <summary>
    /// A localized name of a control in XAML.
    /// </summary>
    /// <returns>The expression; the group is the key.</returns>
    [GeneratedRegex("""AutomationProperties\.Name="\{loc:Loc ([A-Za-z.]+)\}""")]
    private static partial Regex NameKey();
}
