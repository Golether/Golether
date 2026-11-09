using System.Globalization;
using Golether.Media.Streaming.Caching;

namespace Golether.Browser.Media;

/// <summary>
/// Publishes the film of a participant to the <c>&lt;video&gt;</c> element: the service worker of the page answers
/// the requests of the element for <c>stream/&lt;number&gt;</c> with bytes read here, from the cache of the session,
/// which fetches the pieces from the host. This is the browser's counterpart of the <c>golether://</c> protocol of
/// libmpv.
/// </summary>
internal sealed class BrowserStreams : IMediaUriRegistry
{
    /// <summary>
    /// The scheme of the addresses given to the sessions.
    /// </summary>
    public const string SCHEME = "golether-stream";

    /// <summary>
    /// The path the service worker answers, relative to the page.
    /// </summary>
    public const string PAGE_PATH = "stream/";

    /// <summary>
    /// The published films by number.
    /// </summary>
    private readonly Dictionary<int, Published> _streams = [];

    /// <summary>
    /// The number of the last published film.
    /// </summary>
    private int _last;

    /// <inheritdoc />
    public Uri Register(Func<Stream> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var id = ++_last;
        _streams[id] = new Published(factory);
        return new Uri(SCHEME + "://film/" + id.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public void Unregister(Uri uri)
    {
        if (TryParse(uri, out var id) && _streams.Remove(id, out var published))
        {
            published.Dispose();
        }
    }

    /// <summary>
    /// Finds the address on the page of a published film.
    /// </summary>
    /// <param name="uri">The address given to the session.</param>
    /// <param name="pageUrl">The address the element plays.</param>
    /// <returns><see langword="true"/> when the film is published.</returns>
    public bool TryGetPageUrl(Uri uri, out string pageUrl)
    {
        pageUrl = string.Empty;
        if (!TryParse(uri, out var id) || !_streams.ContainsKey(id))
        {
            return false;
        }

        pageUrl = PAGE_PATH + id.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>
    /// Gives the size of a published film.
    /// </summary>
    /// <param name="id">The number of the film.</param>
    /// <returns>The size, or -1 when the film is not published.</returns>
    public long Length(int id) => _streams.TryGetValue(id, out var published) ? published.Stream.Length : -1;

    /// <summary>
    /// Reads a part of a published film; the parts are read one after another.
    /// </summary>
    /// <param name="id">The number of the film.</param>
    /// <param name="offset">The position of the part.</param>
    /// <param name="buffer">Receives the bytes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of bytes read; zero at the end.</returns>
    /// <exception cref="IOException">The film is not published.</exception>
    public async Task<int> ReadAsync(int id, long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!_streams.TryGetValue(id, out var published))
        {
            throw new IOException("The film is not published.");
        }

        await published.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stream = published.Stream;
            stream.Position = offset;
            return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            published.Gate.Release();
        }
    }

    /// <summary>
    /// Tells how much of a published film is cached from a point of it on, and has the cache read ahead from there
    /// while less than wanted is: the decoder of the page reads too little ahead for the cache to start on its own.
    /// </summary>
    /// <param name="pageUrl">The address the element plays.</param>
    /// <param name="fraction">Where the film is, as a part of its size from 0 to 1.</param>
    /// <param name="wanted">How much is wanted ahead, as a part of the size.</param>
    /// <param name="ahead">How much is cached one piece after another from there, as a part of the size.</param>
    /// <returns><see langword="true"/> when the film is published and read already, so its cache can tell.</returns>
    public bool TryGetCachedAhead(string pageUrl, double fraction, double wanted, out double ahead)
    {
        ahead = 0;
        if (!pageUrl.StartsWith(PAGE_PATH, StringComparison.Ordinal) ||
            !int.TryParse(pageUrl.AsSpan(PAGE_PATH.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
            !_streams.TryGetValue(id, out var published) ||
            published.Opened is not MediaReadStream { Length: > 0 } stream)
        {
            return false;
        }

        var reader = stream.Reader;
        var length = stream.Length;
        var position = (long)(Math.Clamp(fraction, 0, 1) * length);
        var bytes = reader.GetBufferedBytesAhead(position);
        if (bytes < wanted * length)
        {
            reader.ReadAheadFrom(position);
        }

        ahead = (double)bytes / length;
        return true;
    }

    /// <summary>
    /// Reads the number of a film from its address.
    /// </summary>
    /// <param name="uri">The address.</param>
    /// <param name="id">The number.</param>
    /// <returns><see langword="true"/> when the address is of this registry.</returns>
    private static bool TryParse(Uri uri, out int id)
    {
        id = 0;
        return uri.Scheme == SCHEME && int.TryParse(uri.AbsolutePath.Trim('/'), NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }

    /// <summary>
    /// A published film: the stream is opened on the first read and shared by all reads.
    /// </summary>
    private sealed class Published : IDisposable
    {
        /// <summary>
        /// Opens the stream.
        /// </summary>
        private readonly Func<Stream> _factory;

        /// <summary>
        /// The stream, once opened.
        /// </summary>
        private Stream? _stream;

        /// <summary>
        /// Initializes a new instance of the <see cref="Published"/> class.
        /// </summary>
        /// <param name="factory">Opens the stream.</param>
        public Published(Func<Stream> factory)
        {
            _factory = factory;
        }

        /// <summary>
        /// Gets the gate that lets one read through at a time.
        /// </summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>
        /// Gets the stream, opening it on the first use.
        /// </summary>
        public Stream Stream => _stream ??= _factory();

        /// <summary>
        /// Gets the stream if it is open already, without opening it.
        /// </summary>
        public Stream? Opened => _stream;

        /// <summary>
        /// Closes the stream.
        /// </summary>
        public void Dispose()
        {
            _stream?.Dispose();
            Gate.Dispose();
        }
    }
}
