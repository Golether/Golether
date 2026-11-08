using System.Runtime.InteropServices.JavaScript;
using Golether.Browser.Interop;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Transports;
using Golether.Transports.WebRtc;

namespace Golether.Browser.Transport;

/// <summary>
/// The way of a guest page to its host: the signalling the invitation names, and a WebRTC connection made by
/// <c>rtc.js</c>, whose first stream proves the key of the host against the invitation. A browser has no other way:
/// it can neither reach an address directly nor punch through the rendezvous server.
/// </summary>
internal sealed class WebRtcConnector : IPeerConnector, ISignalConnector
{
    /// <summary>
    /// The servers of the page.
    /// </summary>
    private readonly BrowserConfig _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRtcConnector"/> class.
    /// </summary>
    /// <param name="config">The servers of the page.</param>
    public WebRtcConnector(BrowserConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public Task<PeerStream> ConnectAsync(PeerEndpoint endpoint, PeerId expectedPeer, StreamPurpose purpose, CancellationToken cancellationToken)
        => Task.FromException<PeerStream>(new IOException("A browser cannot connect to an address directly."));

    /// <inheritdoc />
    public async Task<PeerStream> ConnectAsync(SignalRoute route, PeerId expectedPeer, StreamPurpose purpose, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        cancellationToken.ThrowIfCancellationRequested();
        int id;
        try
        {
            id = await RtcInterop.ConnectAsync(route.Signal.OriginalString, _config.StunUrl, route.Room, expectedPeer.Value, (int)purpose).ConfigureAwait(false);
        }
        catch (JSException ex)
        {
            throw RtcErrors.Translate(ex);
        }

        var stream = new DataChannelStream(DataChannels.Open(id));
        if (cancellationToken.IsCancellationRequested)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return new PeerStream(expectedPeer, purpose, stream, "WebRTC");
    }
}
