using System.Runtime.InteropServices.JavaScript;
using System.Threading.Channels;
using Golether.Browser.Interop;
using Golether.Transports;

namespace Golether.Browser.Transport;

/// <summary>
/// The "listener" of a host in a browser: it holds a room on the rendezvous server and hands the host session every
/// stream a proven participant opens. There is no port: participants reach the host only through the room.
/// </summary>
internal sealed class WebRtcListener : IPeerListener
{
    /// <summary>
    /// The streams waiting for the session.
    /// </summary>
    private readonly Channel<PeerStream> _accepted = Channel.CreateUnbounded<PeerStream>();

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRtcListener"/> class.
    /// </summary>
    private WebRtcListener()
    {
    }

    /// <summary>
    /// Gets the listener of the running session, or <see langword="null"/> when this page hosts nothing.
    /// </summary>
    public static WebRtcListener? Current { get; private set; }

    /// <summary>
    /// Gets zero: a browser listens on no port.
    /// </summary>
    public int Port => 0;

    /// <summary>
    /// Takes a room and starts accepting participants.
    /// </summary>
    /// <param name="signal">The signalling the room is taken at.</param>
    /// <param name="stunUrl">The STUN server.</param>
    /// <param name="room">The room.</param>
    /// <returns>The listener.</returns>
    /// <exception cref="IOException">The room was not taken.</exception>
    public static async Task<WebRtcListener> OpenAsync(Uri signal, string stunUrl, string room)
    {
        ArgumentNullException.ThrowIfNull(signal);

        try
        {
            await RtcInterop.HostOpenAsync(signal.OriginalString, stunUrl, room).ConfigureAwait(false);
        }
        catch (JSException ex)
        {
            throw RtcErrors.Translate(ex);
        }

        var listener = new WebRtcListener();
        Current = listener;
        return listener;
    }

    /// <summary>
    /// Takes a stream a proven participant opened.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <returns><see langword="false"/> when the session is over and the stream must be closed.</returns>
    public bool Offer(PeerStream stream) => _accepted.Writer.TryWrite(stream);

    /// <inheritdoc />
    public ValueTask<PeerStream> AcceptAsync(CancellationToken cancellationToken) => _accepted.Reader.ReadAsync(cancellationToken);

    /// <summary>
    /// Releases the room and closes the connections.
    /// </summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        _accepted.Writer.TryComplete();
        RtcInterop.HostClose();
        return ValueTask.CompletedTask;
    }
}
