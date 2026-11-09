using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// The page around the canvas (<c>page.js</c>): its address, its settings, the clipboard, the full screen, the
/// sleep of the display and the bytes of the film the service worker asks for.
/// </summary>
internal static partial class PageInterop
{
    /// <summary>
    /// Gives the servers the page was published with (<c>config.json</c>).
    /// </summary>
    /// <returns>The JSON of the file.</returns>
    [JSImport("page.config", JsModule.NAME)]
    public static partial string Config();

    /// <summary>
    /// Takes the invitation the page was opened with (after the <c>#</c>) and removes it from the address bar, so it
    /// is not left in the history.
    /// </summary>
    /// <returns>The invitation, or an empty string.</returns>
    [JSImport("page.takeInvitation", JsModule.NAME)]
    public static partial string TakeInvitation();

    /// <summary>
    /// Gives the address of this page without its fragment: an invitation link of a browser host is this address
    /// with the invitation after <c>#</c>.
    /// </summary>
    /// <returns>The address.</returns>
    [JSImport("page.address", JsModule.NAME)]
    public static partial string Address();

    /// <summary>
    /// Reads a setting of this browser.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    [JSImport("page.getSetting", JsModule.NAME)]
    public static partial string? GetSetting(string key);

    /// <summary>
    /// Stores a setting of this browser.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    [JSImport("page.setSetting", JsModule.NAME)]
    public static partial void SetSetting(string key, string value);

    /// <summary>
    /// Puts a text on the clipboard.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the browser took the text.</returns>
    [JSImport("page.copy", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    public static partial Task CopyAsync(string text);

    /// <summary>
    /// Reads the text on the clipboard; the browser asks the user the first time.
    /// </summary>
    /// <returns>The text, or an empty string when the browser refused.</returns>
    [JSImport("page.paste", JsModule.NAME)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string> PasteAsync();

    /// <summary>
    /// Enters or leaves the full screen.
    /// </summary>
    [JSImport("page.toggleFullScreen", JsModule.NAME)]
    public static partial void ToggleFullScreen();

    /// <summary>
    /// Keeps the display on during a session, or lets it sleep again.
    /// </summary>
    /// <param name="awake">Whether to keep the display on.</param>
    [JSImport("page.keepAwake", JsModule.NAME)]
    public static partial void KeepAwake(bool awake);

    /// <summary>
    /// Shows that the page could not start; there are no language packs before .NET runs, so the reason is a code.
    /// </summary>
    /// <param name="code">The reason.</param>
    [JSImport("page.failed", JsModule.NAME)]
    public static partial void Failed(string code);

    /// <summary>
    /// Takes the start screen of the page away: the canvas is drawn.
    /// </summary>
    [JSImport("page.ready", JsModule.NAME)]
    public static partial void Ready();

    /// <summary>
    /// Answers a read of the service worker; the page copies the bytes before returning.
    /// </summary>
    /// <param name="request">The number of the read.</param>
    /// <param name="data">The bytes; empty at the end of the film.</param>
    [JSImport("page.readDone", JsModule.NAME)]
    public static partial void ReadDone(int request, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    /// <summary>
    /// Fails a read of the service worker.
    /// </summary>
    /// <param name="request">The number of the read.</param>
    [JSImport("page.readFailed", JsModule.NAME)]
    public static partial void ReadFailed(int request);

    /// <summary>
    /// Plays a short sound.
    /// </summary>
    /// <param name="wave">The sound as a WAV file.</param>
    [JSImport("page.playSound", JsModule.NAME)]
    public static partial void PlaySound(byte[] wave);

    /// <summary>
    /// Tells whether the page is in full screen now.
    /// </summary>
    /// <returns><see langword="true"/> in full screen.</returns>
    [JSImport("page.isFullScreen", JsModule.NAME)]
    public static partial bool IsFullScreen();
}
