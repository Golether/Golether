using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using Golether.Browser.Interop;
using Golether.Client.Services;
using Golether.Core.Identity;
using Golether.Localization;

namespace Golether.Browser;

/// <summary>
/// The entry point of the WebAssembly part of the page: reads the servers and the device key, chooses the language and
/// draws the screens into the page.
/// </summary>
public static class Program
{
    /// <summary>
    /// The element of the page Avalonia draws into.
    /// </summary>
    private const string HOST_ELEMENT = "out";

    /// <summary>
    /// The font of the texts: a page has no fonts of the system, so the font of the desktop application is named.
    /// </summary>
    private const string DEFAULT_FONT = "fonts:Inter#Inter";

    /// <summary>
    /// Runs when the page started .NET and gave it its modules.
    /// </summary>
    /// <returns>A task that completes when the screens are drawn.</returns>
    public static async Task Main()
    {
        BrowserConfig config;
        try
        {
            config = BrowserConfig.FromJson(PageInterop.Config());
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PageInterop.Failed("Config");
            return;
        }

        var app = BrowserApp.Start(config, PeerId.Parse(await KeyInterop.PeerIdAsync().ConfigureAwait(true)));
        var languages = new LanguageService(Texts.Localizer, app.Settings);
        await languages.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        app.Log.LogInformation("The page started in {Language}", languages.Current.Code);

        await AppBuilder.Configure(() => new App(app, languages))
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = DEFAULT_FONT })
            .StartBrowserAppAsync(HOST_ELEMENT)
            .ConfigureAwait(true);
        PageInterop.Ready();
    }
}
