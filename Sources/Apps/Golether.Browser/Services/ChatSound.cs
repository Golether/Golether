using Golether.Client.Services;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Session;

namespace Golether.Browser.Services;

/// <summary>
/// The clap of a clapperboard when another participant writes in the chat, as in the desktop application: never for
/// the own messages and reactions, at most once a second, and switched off in the settings.
/// </summary>
internal sealed class ChatSound
{
    /// <summary>
    /// The setting that switches the sound off; the same key as in the desktop application.
    /// </summary>
    public const string SETTING = "ui.chat.sound";

    /// <summary>
    /// The shortest time between two sounds.
    /// </summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings;

    /// <summary>
    /// Plays the sound.
    /// </summary>
    private readonly ISoundPlayer _player;

    /// <summary>
    /// The time source.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The sound, read once.
    /// </summary>
    private readonly Lazy<byte[]> _sound = new(ClapperboardSound.Load);

    /// <summary>
    /// When the sound played last, or <see langword="null"/>.
    /// </summary>
    private long? _lastPlayed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatSound"/> class.
    /// </summary>
    /// <param name="messages">The chat of the page.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="player">Plays the sound.</param>
    /// <param name="timeProvider">The time source.</param>
    public ChatSound(ISessionMessages messages, ISettingsStore settings, ISoundPlayer player, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        messages.ChatReceived += (_, entry) => OnChat(entry);
    }

    /// <summary>
    /// Gets a value indicating whether the sound is on.
    /// </summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>
    /// Reads the setting.
    /// </summary>
    /// <returns>A task that completes when the setting is read.</returns>
    public async Task LoadAsync() => Enabled = await _settings.GetAsync(SETTING, CancellationToken.None).ConfigureAwait(true) != "false";

    /// <summary>
    /// Switches the sound on or off and remembers the choice.
    /// </summary>
    /// <param name="value">Whether the sound is on.</param>
    public void SetEnabled(bool value)
    {
        if (Enabled == value)
        {
            return;
        }

        Enabled = value;
        _ = _settings.SetAsync(SETTING, value ? "true" : "false", CancellationToken.None);
    }

    /// <summary>
    /// Plays the sound for a text of another participant.
    /// </summary>
    /// <param name="entry">The message.</param>
    private void OnChat(ChatEntry entry)
    {
        if (!Enabled || entry.IsLocal || entry.Kind != ChatKind.Text)
        {
            return;
        }

        var now = _timeProvider.GetTimestamp();
        if (_lastPlayed is { } last && _timeProvider.GetElapsedTime(last, now) < MinimumInterval)
        {
            return;
        }

        _lastPlayed = now;
        _player.Play(_sound.Value);
    }
}
