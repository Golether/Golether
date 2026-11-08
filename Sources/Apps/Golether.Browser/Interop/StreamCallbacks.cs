using System.Buffers;
using System.Runtime.InteropServices.JavaScript;
using Microsoft.Extensions.Logging;

namespace Golether.Browser.Interop;

/// <summary>
/// What the service worker asks through the page: the size of a published film and its bytes.
/// </summary>
internal static partial class StreamCallbacks
{
    /// <summary>
    /// The longest part answered at once.
    /// </summary>
    private const int MAX_PART = 1024 * 1024;

    /// <summary>
    /// Gives the size of a published film.
    /// </summary>
    /// <param name="stream">The number of the film.</param>
    /// <returns>The size, or -1 when the film is not published.</returns>
    [JSExport]
    public static double Length(int stream) => BrowserApp.Instance?.Streams.Length(stream) ?? -1;

    /// <summary>
    /// Starts a read; the answer goes to <see cref="PageInterop.ReadDone"/> or <see cref="PageInterop.ReadFailed"/>.
    /// </summary>
    /// <param name="request">The number of the read.</param>
    /// <param name="stream">The number of the film.</param>
    /// <param name="offset">The position of the part.</param>
    /// <param name="count">The length of the part.</param>
    [JSExport]
    public static void Read(int request, int stream, double offset, int count)
        => _ = ReadAsync(request, stream, (long)offset, Math.Clamp(count, 0, MAX_PART));

    /// <summary>
    /// Reads a part and answers the page.
    /// </summary>
    /// <param name="request">The number of the read.</param>
    /// <param name="stream">The number of the film.</param>
    /// <param name="offset">The position of the part.</param>
    /// <param name="count">The length of the part.</param>
    /// <returns>A task that completes when the page was answered.</returns>
    private static async Task ReadAsync(int request, int stream, long offset, int count)
    {
        if (BrowserApp.Instance is not { } app || offset < 0)
        {
            PageInterop.ReadFailed(request);
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(count, 1));
        try
        {
            var read = await app.Streams.ReadAsync(stream, offset, buffer.AsMemory(0, count), CancellationToken.None).ConfigureAwait(false);
            PageInterop.ReadDone(request, buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
        {
            app.Log.LogWarning("A read of the film failed: {Reason}", ex.Message);
            PageInterop.ReadFailed(request);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
