using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Golether.Browser.ViewModels;
using Golether.Browser.Views;
using Golether.Client.Services;

namespace Golether.Browser;

/// <summary>
/// The Avalonia application of the page: one view, as on a phone, laid out like the main window of the desktop.
/// </summary>
public sealed partial class App : Application
{
    /// <summary>
    /// The page.
    /// </summary>
    private readonly BrowserApp? _app;

    /// <summary>
    /// The language of the page.
    /// </summary>
    private readonly ILanguageService? _languages;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class for the designer.
    /// </summary>
    public App()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class.
    /// </summary>
    /// <param name="app">The page.</param>
    /// <param name="languages">The language of the page.</param>
    internal App(BrowserApp app, ILanguageService languages)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _languages = languages ?? throw new ArgumentNullException(nameof(languages));
    }

    /// <summary>
    /// Loads the styles of the application.
    /// </summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Puts the main view into the page.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime single && _app is not null && _languages is not null)
        {
            var viewModel = new MainViewModel(_app, _languages);
            single.MainView = new MainView { DataContext = viewModel };
            viewModel.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
