using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json;
using Golether.Browser.Media;
using Golether.Browser.Session;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Localization;
using Golether.Security.Invites;
using Golether.Sync.Protocol;

namespace Golether.Browser.Interop;

/// <summary>
/// What the page asks of .NET: start, words, the session and the intentions of the user. A failure reaches the page
/// as a rejected promise with the reason in the language of the page.
/// </summary>
internal static partial class BrowserApi
{
    /// <summary>
    /// The language packs.
    /// </summary>
    private static readonly LanguageCatalog _languages = LanguageCatalog.LoadEmbedded();

    /// <summary>
    /// Starts the page: the language, the servers and the device key.
    /// </summary>
    /// <param name="language">The language the user chose before, or the language of the browser.</param>
    /// <param name="signalUrl">The WebSocket of the signalling.</param>
    /// <param name="stunUrl">The STUN server.</param>
    /// <param name="meet">The rendezvous server written into invitations.</param>
    /// <returns>The code of the language chosen.</returns>
    [JSExport]
    public static async Task<string> StartAsync(string language, string signalUrl, string stunUrl, string meet)
    {
        var code = SetLanguage(language);
        var config = BrowserConfig.Parse(signalUrl, stunUrl, meet);

        var peer = PeerId.Parse(await KeyInterop.PeerIdAsync().ConfigureAwait(false));
        BrowserApp.Start(config, peer);
        return code;
    }

    /// <summary>
    /// Switches the language of the page.
    /// </summary>
    /// <param name="language">A language code such as <c>ru</c> or <c>pt-PT</c>.</param>
    /// <returns>The code of the language chosen: English when there is no pack of the language.</returns>
    [JSExport]
    public static string SetLanguage(string language)
    {
        var code = (language ?? string.Empty).Split('-')[0].ToLowerInvariant();
        if (_languages.Find(code) is null)
        {
            code = LanguageCatalog.DEFAULT_CODE;
        }

        Texts.Localizer = new Localizer(_languages, code);
        return code;
    }

    /// <summary>
    /// Lists the languages as <c>code=name</c>.
    /// </summary>
    /// <returns>The languages.</returns>
    [JSExport]
    public static string[] Languages() => [.. _languages.Languages.Select(l => l.Code + "=" + l.NativeName)];

    /// <summary>
    /// Gives a text of the page.
    /// </summary>
    /// <param name="key">The key of the text.</param>
    /// <returns>The text in the language of the page.</returns>
    [JSExport]
    public static string Text(string key) => Texts.Get(key);

    /// <summary>
    /// Gives a text of the page with its placeholders filled.
    /// </summary>
    /// <param name="key">The key of the text.</param>
    /// <param name="args">The values of <c>{0}</c>, <c>{1}</c>, ….</param>
    /// <returns>The text in the language of the page.</returns>
    [JSExport]
    public static string Format(string key, string[] args) => Texts.Format(key, [.. args]);

    /// <summary>
    /// Reads an invitation without joining: the page shows whose session it is.
    /// </summary>
    /// <param name="payload">The part of the link after <c>#</c>.</param>
    /// <returns>JSON with <c>text</c> (the question to the user) and <c>expired</c>.</returns>
    /// <exception cref="FormatException">The invitation is damaged.</exception>
    [JSExport]
    public static string Describe(string payload)
    {
        var invite = ParseInvite(payload);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("text", Texts.Format("Main.Link.Join.Text", invite.SessionName, invite.HostName));
            json.WriteBoolean("expired", invite.ExpiresAt <= TimeProvider.System.GetUtcNow());

            // A browser reaches a host only through the signalling; an invitation without it leads to the application.
            if (invite.Signal is null)
            {
                json.WriteString("blocked", Texts.Get(invite.Rendezvous is null ? "Browser.Join.DirectOnly" : "Browser.Join.PrivateServer"));
            }
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Creates a session on this page and shows a chosen file.
    /// </summary>
    /// <param name="sessionName">The session name.</param>
    /// <param name="displayName">The name of the host.</param>
    /// <param name="file">The number of the file on the page.</param>
    /// <param name="fileName">The name of the file.</param>
    /// <param name="length">The size of the file.</param>
    /// <returns>A task that completes when the film is shown.</returns>
    [JSExport]
    public static async Task HostAsync(string sessionName, string displayName, int file, string fileName, double length)
    {
        var app = await LeaveCurrentAsync().ConfigureAwait(false);

        var session = await BrowserSession.HostAsync(app, sessionName, displayName).ConfigureAwait(false);
        app.Session = session;
        await session.ShareAsync(BrowserFiles.Add(file, fileName, (long)length)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gives the invitation of the hosted session.
    /// </summary>
    /// <param name="renew">Whether to make a new one.</param>
    /// <returns>The part of the link after <c>#</c>.</returns>
    [JSExport]
    public static string Invite(bool renew) => Current.Invite(renew);

    /// <summary>
    /// Joins the session of an invitation; completes when the host let this page in.
    /// </summary>
    /// <param name="payload">The part of the link after <c>#</c>.</param>
    /// <param name="displayName">The name of the guest.</param>
    /// <returns>A task that fails with the reason when the session was not joined.</returns>
    [JSExport]
    public static async Task JoinAsync(string payload, string displayName)
    {
        var invite = ParseInvite(payload);
        var app = await LeaveCurrentAsync().ConfigureAwait(false);

        var session = BrowserSession.CreateGuest(app, displayName);
        app.Session = session;
        await session.JoinAsync(invite).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the state of the session.
    /// </summary>
    /// <returns>The JSON of <see cref="SnapshotJson"/>, or an empty string without a session.</returns>
    [JSExport]
    public static string Snapshot() => BrowserApp.Instance?.Session is { } session ? session.Snapshot() : string.Empty;

    /// <summary>
    /// Switches the microphone and the camera of this page; called from a click, which a phone needs to give them.
    /// </summary>
    /// <param name="microphone">Whether the microphone should be on.</param>
    /// <param name="camera">Whether the camera should be on.</param>
    [JSExport]
    public static void SetDevices(bool microphone, bool camera) => BrowserApp.Instance?.Session?.SetDevices(microphone, camera);

    /// <summary>
    /// Gives the reactions a participant may send.
    /// </summary>
    /// <returns>The reactions.</returns>
    [JSExport]
    public static string[] Reactions() => [.. ChatMessage.Reactions];

    /// <summary>
    /// Sends a chat message or a reaction.
    /// </summary>
    /// <param name="kind">The value of <see cref="ChatKind"/>.</param>
    /// <param name="text">The text, or the reaction.</param>
    /// <returns>A task that completes when the message is sent; a malformed one fails with the reason.</returns>
    [JSExport]
    public static Task SendChatAsync(int kind, string text)
        => Enum.IsDefined((ChatKind)kind) ? Current.SendChatAsync((ChatKind)kind, text ?? string.Empty) : Task.CompletedTask;

    /// <summary>
    /// Switches the microphone or the camera of a guest off; only the host can.
    /// </summary>
    /// <param name="peer">The guest.</param>
    /// <param name="microphone">Whether to switch the microphone off.</param>
    /// <param name="camera">Whether to switch the camera off.</param>
    /// <returns>A task that completes when the request is sent.</returns>
    [JSExport]
    public static Task SwitchOffAsync(string peer, bool microphone, bool camera)
        => PeerId.TryParse(peer, out var id) ? Current.SwitchOffAsync(id, microphone, camera) : Task.CompletedTask;

    /// <summary>
    /// Passes an intention of the user: play, pause, seek, start without waiting.
    /// </summary>
    /// <param name="kind">The value of <see cref="PlaybackRequestKind"/>.</param>
    /// <param name="position">The position in seconds, or a negative number when the intention has none.</param>
    /// <returns>A task that completes when the intention was passed on.</returns>
    [JSExport]
    public static Task RequestAsync(int kind, double position)
    {
        if (!Enum.IsDefined((PlaybackRequestKind)kind))
        {
            return Task.CompletedTask;
        }

        TimeSpan? at = position >= 0 && double.IsFinite(position) ? TimeSpan.FromSeconds(position) : null;
        return Current.RequestAsync(new PlaybackRequest((PlaybackRequestKind)kind, at));
    }

    /// <summary>
    /// Takes the answer of the host about a guest.
    /// </summary>
    /// <param name="request">The number of the question.</param>
    /// <param name="approve">Whether the guest is let in.</param>
    [JSExport]
    public static void Answer(int request, bool approve) => BrowserApp.Instance?.Prompt.Answer(request, approve);

    /// <summary>
    /// Leaves the session; a host ends it for everybody.
    /// </summary>
    /// <returns>A task that completes when the session is closed.</returns>
    [JSExport]
    public static async Task LeaveAsync() => await LeaveCurrentAsync().ConfigureAwait(false);

    /// <summary>
    /// Gets the current session.
    /// </summary>
    private static BrowserSession Current => BrowserApp.Instance?.Session ?? throw new InvalidOperationException("There is no session.");

    /// <summary>
    /// Closes the current session, if any.
    /// </summary>
    /// <returns>The page.</returns>
    private static async Task<BrowserApp> LeaveCurrentAsync()
    {
        var app = BrowserApp.Instance ?? throw new InvalidOperationException("The page has not started.");
        if (app.Session is { } session)
        {
            app.Session = null;
            await session.DisposeAsync().ConfigureAwait(false);
        }

        return app;
    }

    /// <summary>
    /// Parses the part of a link after <c>#</c>.
    /// </summary>
    /// <param name="payload">The part of the link.</param>
    /// <returns>The invitation.</returns>
    /// <exception cref="FormatException">The invitation is damaged.</exception>
    private static Invite ParseInvite(string payload)
        => Security.Invites.Invite.ParseLink(Security.Invites.Invite.LINK_PREFIX + (payload ?? string.Empty).Trim().TrimStart('#'));
}
