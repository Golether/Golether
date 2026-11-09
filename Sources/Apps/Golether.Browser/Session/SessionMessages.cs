using Golether.Client.Services;
using Golether.Core.Data.Enums;
using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.Browser.Session;

/// <summary>
/// The chat and the strokes of whichever session the page has now: the shared view models are made once with the
/// page, the sessions come and go.
/// </summary>
internal sealed class SessionMessages : ISessionMessages
{
    /// <summary>
    /// The page.
    /// </summary>
    private readonly BrowserApp _app;

    /// <summary>
    /// The session whose messages are passed on, or <see langword="null"/>.
    /// </summary>
    private BrowserSession? _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionMessages"/> class.
    /// </summary>
    /// <param name="app">The page.</param>
    public SessionMessages(BrowserApp app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
    }

    /// <inheritdoc />
    public event EventHandler<ChatEntry>? ChatReceived;

    /// <inheritdoc />
    public event EventHandler<StrokeUpdate>? DrawReceived;

    /// <summary>
    /// Passes on the messages of a session.
    /// </summary>
    /// <param name="session">The session.</param>
    public void Attach(BrowserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        Detach();
        _session = session;
        session.ChatReceived += OnChat;
        session.DrawReceived += OnDraw;
    }

    /// <summary>
    /// Stops passing on the messages of the session.
    /// </summary>
    public void Detach()
    {
        if (_session is { } session)
        {
            session.ChatReceived -= OnChat;
            session.DrawReceived -= OnDraw;
            _session = null;
        }
    }

    /// <inheritdoc />
    public Task SendChatAsync(ChatKind kind, string text, CancellationToken cancellationToken)
        => _app.Session?.SendChatAsync(kind, text, cancellationToken) ?? Task.CompletedTask;

    /// <inheritdoc />
    public Task SendDrawAsync(string strokeId, StrokePhase phase, IReadOnlyList<StrokePoint> points, CancellationToken cancellationToken)
        => _app.Session?.SendDrawAsync(strokeId, phase, points, cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// Passes on a chat message.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="entry">The message.</param>
    private void OnChat(object? sender, ChatEntry entry) => ChatReceived?.Invoke(this, entry);

    /// <summary>
    /// Passes on a part of a stroke.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="update">The part.</param>
    private void OnDraw(object? sender, StrokeUpdate update) => DrawReceived?.Invoke(this, update);
}
