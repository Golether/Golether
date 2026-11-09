using Microsoft.Extensions.Logging;
using Golether.Browser.Interop;
using Golether.Browser.Logging;
using Golether.Browser.Media;
using Golether.Browser.Services;
using Golether.Browser.Session;
using Golether.Core.Identity;
using Golether.Media.Conference.Browser;

namespace Golether.Browser;

/// <summary>
/// The composition root of the page: the servers, the device key, the player, the cameras and the session. One page
/// runs one session at a time.
/// </summary>
internal sealed class BrowserApp
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserApp"/> class.
    /// </summary>
    /// <param name="config">The servers of the page.</param>
    /// <param name="localPeer">The identifier of the device key of the browser.</param>
    private BrowserApp(BrowserConfig config, PeerId localPeer)
    {
        Config = config;
        LocalPeer = localPeer;
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddProvider(new BrowserLoggerProvider(LogLevel.Information)));
        Log = LoggerFactory.CreateLogger<BrowserApp>();
        Streams = new BrowserStreams();
        Player = new BrowserPlayer(Streams);
        Conference = new BrowserConferenceMedia(new JsBrowserRtc(), config.StunUrl, LoggerFactory.CreateLogger<BrowserConferenceMedia>());
    }

    /// <summary>
    /// Gets the page once it has started, or <see langword="null"/> before.
    /// </summary>
    public static BrowserApp? Instance { get; private set; }

    /// <summary>
    /// Gets the servers of the page.
    /// </summary>
    public BrowserConfig Config { get; }

    /// <summary>
    /// Gets the identifier of the device key of the browser.
    /// </summary>
    public PeerId LocalPeer { get; }

    /// <summary>
    /// Gets the logging of the page (the console of the browser).
    /// </summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Gets the log of the page itself.
    /// </summary>
    public ILogger Log { get; }

    /// <summary>
    /// Gets the films published to the service worker.
    /// </summary>
    public BrowserStreams Streams { get; }

    /// <summary>
    /// Gets the film element the sessions control.
    /// </summary>
    public BrowserPlayer Player { get; }

    /// <summary>
    /// Gets the volume and the sound track of the film on this page.
    /// </summary>
    public BrowserLocalControls LocalControls { get; } = new();

    /// <summary>
    /// Gets the cameras, the voices and the data channels.
    /// </summary>
    public BrowserConferenceMedia Conference { get; }

    /// <summary>
    /// Gets the questions to the host about guests.
    /// </summary>
    public BrowserAdmissionPrompt Prompt { get; } = new();

    /// <summary>
    /// Gets the settings kept by this browser.
    /// </summary>
    public BrowserSettingsStore Settings { get; } = new();

    /// <summary>
    /// Gets or sets the session of the page, or <see langword="null"/> while there is none.
    /// </summary>
    public BrowserSession? Session { get; set; }

    /// <summary>
    /// Starts the page.
    /// </summary>
    /// <param name="config">The servers of the page.</param>
    /// <param name="localPeer">The identifier of the device key of the browser.</param>
    /// <returns>The page.</returns>
    public static BrowserApp Start(BrowserConfig config, PeerId localPeer)
    {
        Instance = new BrowserApp(config, localPeer);
        return Instance;
    }
}
