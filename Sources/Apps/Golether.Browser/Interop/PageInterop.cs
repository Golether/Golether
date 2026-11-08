using System.Runtime.InteropServices.JavaScript;

namespace Golether.Browser.Interop;

/// <summary>
/// The page itself (<c>app.js</c>): it redraws what changed, asks the host about guests and hands the bytes of the
/// film to the service worker.
/// </summary>
internal static partial class PageInterop
{
    /// <summary>
    /// Tells the page that the session changed; it reads the snapshot when it redraws.
    /// </summary>
    [JSImport("page.changed", JsModule.NAME)]
    public static partial void Changed();

    /// <summary>
    /// Adds a line to the event feed.
    /// </summary>
    /// <param name="text">The line, in the language of the page.</param>
    [JSImport("page.event", JsModule.NAME)]
    public static partial void Event(string text);

    /// <summary>
    /// Asks the host whether to let a guest in.
    /// </summary>
    /// <param name="request">The number of the question, for <see cref="BrowserApi.Answer"/>.</param>
    /// <param name="name">The name of the guest.</param>
    /// <param name="code">The verification code to read aloud.</param>
    [JSImport("page.ask", JsModule.NAME)]
    public static partial void Ask(int request, string name, string code);

    /// <summary>
    /// Shows a chat message or a reaction.
    /// </summary>
    /// <param name="message">The message as JSON (<see cref="Session.PageEventsJson.Chat"/>).</param>
    [JSImport("page.chat", JsModule.NAME)]
    public static partial void Chat(string message);

    /// <summary>
    /// Draws a part of a pen stroke over the picture.
    /// </summary>
    /// <param name="stroke">The part as JSON (<see cref="Session.PageEventsJson.Stroke"/>).</param>
    [JSImport("page.stroke", JsModule.NAME)]
    public static partial void Stroke(string stroke);

    /// <summary>
    /// Shows the picture a participant chose to show while its camera is off.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="image">The PNG in base64, or an empty string when the picture was reset.</param>
    [JSImport("page.avatar", JsModule.NAME)]
    public static partial void Avatar(string peer, string image);

    /// <summary>
    /// Takes a question back: the guest left or the time ran out.
    /// </summary>
    /// <param name="request">The number of the question.</param>
    [JSImport("page.cancelAsk", JsModule.NAME)]
    public static partial void CancelAsk(int request);

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
}
