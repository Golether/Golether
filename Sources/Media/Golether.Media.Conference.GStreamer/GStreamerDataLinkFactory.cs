using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Media.Conference.GStreamer.Native;
using Golether.Transports.WebRtc;

namespace Golether.Media.Conference.GStreamer;

/// <summary>
/// Makes the WebRTC data connections of sessions with <c>webrtcbin</c>. Without GStreamer it is not available, and
/// a session simply offers no WebRTC way in: everything else works as before.
/// </summary>
public sealed class GStreamerDataLinkFactory : IDataLinkFactory
{
    /// <summary>
    /// The log factory.
    /// </summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GStreamerDataLinkFactory"/> class, starting GStreamer if the
    /// cameras did not yet.
    /// </summary>
    /// <param name="options">Where GStreamer is; the same settings as of the cameras.</param>
    /// <param name="loggerFactory">The log factory.</param>
    public GStreamerDataLinkFactory(GStreamerConferenceOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        IsAvailable = GstRuntime.TryInitialize(options.BundleRoot, options.RegistryFile, out var error);
        UnavailableReason = error;
    }

    /// <summary>
    /// Gets a value indicating whether connections can be made.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// Gets why connections cannot be made, or <see langword="null"/>.
    /// </summary>
    public string? UnavailableReason { get; }

    /// <inheritdoc />
    public IDataLink Create(string? stunServer, string? turnServer)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("GStreamer is not available: " + UnavailableReason);
        }

        return new GStreamerDataLink(stunServer, turnServer, _loggerFactory.CreateLogger<GStreamerDataLink>());
    }
}
