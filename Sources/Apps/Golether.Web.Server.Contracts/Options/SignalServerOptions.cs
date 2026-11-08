using Golether.Localization;

namespace Golether.Web.Server.Options;

/// <summary>
/// Settings of the signalling of browsers, read from the <c>Signal</c> section of the configuration. A browser cannot
/// open a TCP connection of its own, so it meets the host over a WebSocket of this server: the sides pass each other
/// the descriptions of a WebRTC connection and then talk directly. The server sees the descriptions (addresses and
/// fingerprints of keys) and nothing of the session.
/// </summary>
public sealed record SignalServerOptions
{
    /// <summary>
    /// The name of the configuration section.
    /// </summary>
    public const string SECTION = "Signal";

    /// <summary>
    /// The path of the WebSocket.
    /// </summary>
    public const string PATH = "/signal";

    /// <summary>
    /// Gets a value indicating whether the server takes browsers (default off).
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets the address to listen on: <c>http://</c> for a try on one computer, <c>https://</c> with the certificate
    /// of <c>Kestrel:Certificates:Default</c> for the internet (a page served over HTTPS opens only <c>wss://</c>).
    /// </summary>
    public string Url { get; init; } = "http://0.0.0.0:47943";

    /// <summary>
    /// Gets the pages allowed to use the server, as origins (<c>https://golether.github.io</c>). A request of
    /// another page is refused; a request without an origin comes from a program, not from a page, and is served.
    /// </summary>
    public string[] AllowedOrigins { get; init; } = ["http://localhost:47950", "http://127.0.0.1:47950"];

    /// <summary>
    /// Gets the number of open WebSockets served at once (default 1024).
    /// </summary>
    public int MaxConnections { get; init; } = 1024;

    /// <summary>
    /// Gets the number of rooms held at once (default 1024; one hosted session holds one room).
    /// </summary>
    public int MaxRooms { get; init; } = 1024;

    /// <summary>
    /// Gets the number of meetings open at once in one room (default 16).
    /// </summary>
    public int MaxMeetingsPerRoom { get; init; } = 16;

    /// <summary>
    /// Gets the number of new WebSockets one IP address may open per minute (default 120).
    /// </summary>
    public int MaxConnectionsPerMinute { get; init; } = 120;

    /// <summary>
    /// Gets the time a device has to say what it wants after connecting (default 10 s).
    /// </summary>
    public TimeSpan FirstMessageTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the time a meeting may last: the descriptions are passed in seconds, after that the sides talk directly
    /// (default 60 s).
    /// </summary>
    public TimeSpan MeetingTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets the port of <see cref="Url"/>, or <see langword="null"/> when the address is not valid.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? Port => Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.Port : null;

    /// <summary>
    /// Checks whether a page may use the server.
    /// </summary>
    /// <param name="origin">The origin the browser sent, or <see langword="null"/> when there was none.</param>
    /// <returns><see langword="true"/> when the request is served.</returns>
    public bool IsAllowedOrigin(string? origin)
        => string.IsNullOrEmpty(origin) || AllowedOrigins.Any(allowed => string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Checks the settings.
    /// </summary>
    /// <returns>The problems in the language of the system; empty when the settings are valid.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!Enabled)
        {
            return problems;
        }

        if (Port is null)
        {
            problems.Add(Texts.Format("Server.Error.BadAddress", Key(nameof(Url)), Url));
        }

        OptionChecks.Range(problems, Key(nameof(MaxConnections)), MaxConnections, 1, 1_000_000);
        OptionChecks.Range(problems, Key(nameof(MaxRooms)), MaxRooms, 1, 1_000_000);
        OptionChecks.Range(problems, Key(nameof(MaxMeetingsPerRoom)), MaxMeetingsPerRoom, 1, 1000);
        OptionChecks.Range(problems, Key(nameof(MaxConnectionsPerMinute)), MaxConnectionsPerMinute, 1, 1_000_000);
        OptionChecks.Time(problems, Key(nameof(FirstMessageTimeout)), FirstMessageTimeout);
        OptionChecks.Time(problems, Key(nameof(MeetingTimeout)), MeetingTimeout);
        return problems;
    }

    /// <summary>
    /// Builds the configuration key of a setting.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <returns>The key, for example <c>Signal:Url</c>.</returns>
    private static string Key(string name) => SECTION + ":" + name;
}
