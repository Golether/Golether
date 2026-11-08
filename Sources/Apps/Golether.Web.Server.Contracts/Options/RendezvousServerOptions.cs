using System.Net;
using Golether.Localization;
using Golether.Transports.Rendezvous.Protocol;

namespace Golether.Web.Server.Options;

/// <summary>
/// Settings of the rendezvous server, read from the <c>Rendezvous</c> section of the configuration
/// (<c>appsettings.json</c>, variables <c>Rendezvous__Port</c>, arguments <c>--Rendezvous:Port=…</c>).
/// </summary>
public sealed record RendezvousServerOptions
{
    /// <summary>
    /// The name of the configuration section.
    /// </summary>
    public const string SECTION = "Rendezvous";

    /// <summary>
    /// Gets the IP address to listen on: <c>0.0.0.0</c> for every IPv4 interface (default) or the address of one
    /// interface. Devices meet over IPv4, so an IPv6 address makes sense only as <c>::</c> (both families).
    /// </summary>
    public string ListenAddress { get; init; } = "0.0.0.0";

    /// <summary>
    /// Gets the port: TCP for meetings and, when <see cref="EnableStun"/>, UDP for STUN (default 47900).
    /// </summary>
    public int Port { get; init; } = RendezvousProtocol.DEFAULT_PORT;

    /// <summary>
    /// Gets a value indicating whether the server answers STUN binding requests on the UDP port, so the cameras and
    /// voices of a session learn their public addresses and go directly too (default on).
    /// </summary>
    public bool EnableStun { get; init; } = true;

    /// <summary>
    /// Gets the number of open TCP connections served at once (default 1024).
    /// </summary>
    public int MaxConnections { get; init; } = 1024;

    /// <summary>
    /// Gets the number of rooms held at once (default 1024; one hosted session holds one room).
    /// </summary>
    public int MaxRooms { get; init; } = 1024;

    /// <summary>
    /// Gets the number of meetings waiting for the host of one room (default 16; a participant opens up to five
    /// streams).
    /// </summary>
    public int MaxPendingPerRoom { get; init; } = 16;

    /// <summary>
    /// Gets the number of new TCP connections one IP address may open per minute (default 120).
    /// </summary>
    public int MaxConnectionsPerMinute { get; init; } = 120;

    /// <summary>
    /// Gets the number of STUN requests one IP address may send per minute (default 600).
    /// </summary>
    public int MaxStunPerMinute { get; init; } = 600;

    /// <summary>
    /// Gets the time a device has to say what it wants after connecting (default 10 s).
    /// </summary>
    public TimeSpan FirstMessageTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the time the host has to answer a meeting (default 20 s).
    /// </summary>
    public TimeSpan AnswerTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Gets the time a host may stay silent before its room is released (default 90 s; hosts ping every 20 s).
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Gets the parsed <see cref="ListenAddress"/>.
    /// </summary>
    /// <exception cref="FormatException">The address is not an IP address.</exception>
    [System.Text.Json.Serialization.JsonIgnore]
    public IPAddress ParsedListenAddress
        => IPAddress.TryParse(ListenAddress, out var address) ? address : throw new FormatException(Texts.Format("Server.Error.BadAddress", Key(nameof(ListenAddress)), ListenAddress));

    /// <summary>
    /// Checks the settings.
    /// </summary>
    /// <returns>The problems in the language of the system; empty when the settings are valid.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!IPAddress.TryParse(ListenAddress, out _))
        {
            problems.Add(Texts.Format("Server.Error.BadAddress", Key(nameof(ListenAddress)), ListenAddress));
        }

        OptionChecks.Range(problems, Key(nameof(Port)), Port, 0, 65535);
        OptionChecks.Range(problems, Key(nameof(MaxConnections)), MaxConnections, 1, 1_000_000);
        OptionChecks.Range(problems, Key(nameof(MaxRooms)), MaxRooms, 1, 1_000_000);
        OptionChecks.Range(problems, Key(nameof(MaxPendingPerRoom)), MaxPendingPerRoom, 1, 1000);
        OptionChecks.Range(problems, Key(nameof(MaxConnectionsPerMinute)), MaxConnectionsPerMinute, 1, 1_000_000);
        OptionChecks.Range(problems, Key(nameof(MaxStunPerMinute)), MaxStunPerMinute, 1, 1_000_000);
        OptionChecks.Time(problems, Key(nameof(FirstMessageTimeout)), FirstMessageTimeout);
        OptionChecks.Time(problems, Key(nameof(AnswerTimeout)), AnswerTimeout);
        OptionChecks.Time(problems, Key(nameof(IdleTimeout)), IdleTimeout);
        return problems;
    }

    /// <summary>
    /// Builds the configuration key of a setting.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <returns>The key, for example <c>Rendezvous:Port</c>.</returns>
    private static string Key(string name) => SECTION + ":" + name;
}
