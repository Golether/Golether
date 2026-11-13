using NSubstitute;
using Golether.Client.Enums;
using Golether.Client.Services;
using Golether.Client.ViewModels;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Media.Player;

namespace Golether.Client.Tests;

/// <summary>
/// Tests of <see cref="TracksViewModel"/>: every participant picks their own sound and subtitles.
/// </summary>
public sealed class TracksViewModelTests
{
    /// <summary>
    /// The player.
    /// </summary>
    private readonly ILocalPlayerControls _player = Substitute.For<ILocalPlayerControls>();

    /// <summary>
    /// The stored settings.
    /// </summary>
    private readonly Dictionary<string, string> _stored = [];

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();

    /// <summary>
    /// The dialogs.
    /// </summary>
    private readonly ICommonDialogs _dialogs = Substitute.For<ICommonDialogs>();

    /// <summary>
    /// The features of the applications; subtitles are offered.
    /// </summary>
    private readonly SwitchableFeatures _features = new(AppOnlyFeature.Subtitles);

    /// <summary>
    /// The tracks the player reports.
    /// </summary>
    private List<MediaTrack> _tracks = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="TracksViewModelTests"/> class.
    /// </summary>
    public TracksViewModelTests()
    {
        _settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => _stored.GetValueOrDefault(call.ArgAt<string>(0)));
        _settings.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _stored[call.ArgAt<string>(0)] = call.ArgAt<string>(1);
                return Task.CompletedTask;
            });
        _player.GetTracks().Returns(_ => _tracks.ToArray());
        _player.When(p => p.SelectTrack(Arg.Any<MediaTrackKind>(), Arg.Any<long?>())).Do(call =>
        {
            var kind = call.ArgAt<MediaTrackKind>(0);
            var id = call.ArgAt<long?>(1);
            _tracks = _tracks.Select(t => t.Kind == kind ? t with { IsSelected = t.Id == id } : t).ToList();
        });
    }

    /// <summary>
    /// The lists show the tracks with the current choice and an "off" entry for subtitles.
    /// </summary>
    [Fact]
    public void Refresh_ShowsTracks()
    {
        _tracks = Film(audioSelected: 1, subtitleSelected: null);
        var model = Create();

        model.Refresh();

        Assert.True(model.HasTracks);
        Assert.Equal(2, model.AudioTracks.Count);
        Assert.True(model.AudioTracks[0].IsSelected);
        Assert.Equal(3, model.SubtitleTracks.Count);
        Assert.Null(model.SubtitleTracks[0].Id);
        Assert.True(model.SubtitleTracks[0].IsSelected);
        Assert.False(model.SubtitlesShown);
    }

    /// <summary>
    /// A choice is applied locally and remembered as a language; the next film starts with it.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Choice_IsRememberedForTheNextFilm()
    {
        _tracks = Film(audioSelected: 1, subtitleSelected: null);
        var model = Create();
        await model.LoadAsync();

        model.AudioTracks[1].SelectCommand.Execute(null);
        model.SubtitleTracks[2].SelectCommand.Execute(null);

        _player.Received().SelectTrack(MediaTrackKind.Audio, 2);
        _player.Received().SelectTrack(MediaTrackKind.Subtitle, 2);
        Assert.Equal("ru", _stored[TracksViewModel.AUDIO_LANGUAGE_SETTING]);
        Assert.Equal("en", _stored[TracksViewModel.SUBTITLE_LANGUAGE_SETTING]);
        Assert.True(model.SubtitlesShown);
        Assert.True(model.AudioTracks[1].IsSelected);

        // The next film lists its tracks in another order.
        var next = Create();
        _tracks =
        [
            new MediaTrack(1, MediaTrackKind.Audio, null, "rus", "ac3", 6, false, false, false),
            new MediaTrack(2, MediaTrackKind.Audio, null, "eng", "ac3", 6, true, false, true),
            new MediaTrack(1, MediaTrackKind.Subtitle, null, "eng", "subrip", null, false, false, false),
        ];
        _player.ClearReceivedCalls();
        await next.LoadAsync();

        _player.Received(1).SelectTrack(MediaTrackKind.Audio, 1);
        _player.Received(1).SelectTrack(MediaTrackKind.Subtitle, 1);
        Assert.True(next.AudioTracks[0].IsSelected);
        Assert.True(next.SubtitlesShown);

        // Preferences are applied once per film: the user may still change the tracks.
        next.AudioTracks[1].SelectCommand.Execute(null);
        Assert.True(next.AudioTracks[1].IsSelected);
    }

    /// <summary>
    /// "No subtitles" is remembered and switches off the default subtitles of the next film, but a file the user
    /// adds later stays shown.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SubtitlesOff_IsRemembered()
    {
        _stored[TracksViewModel.SUBTITLE_LANGUAGE_SETTING] = TracksViewModel.SUBTITLES_OFF;
        _tracks = Film(audioSelected: 1, subtitleSelected: 1);
        var model = Create();

        await model.LoadAsync();

        _player.Received(1).SelectTrack(MediaTrackKind.Subtitle, null);
        Assert.False(model.SubtitlesShown);

        _tracks = [.. _tracks.Select(t => t with { IsSelected = t.Kind == MediaTrackKind.Audio && t.IsSelected }),
            new MediaTrack(3, MediaTrackKind.Subtitle, null, null, "subrip", null, false, true, true)];
        model.Refresh();

        _player.Received(1).SelectTrack(MediaTrackKind.Subtitle, null);
        Assert.True(model.SubtitlesShown);
    }

    /// <summary>
    /// Subtitles from a file are loaded; a failure is shown to the user.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task AddSubtitles_LoadsTheFile()
    {
        var model = Create();
        _dialogs.PickSubtitleFileAsync().Returns("C:\\films\\dune.srt", "C:\\films\\broken.srt");
        _player.When(p => p.AddSubtitleFile("C:\\films\\broken.srt")).Do(_ => throw new IOException("нет доступа"));

        await model.AddSubtitlesCommand.ExecuteAsync(null);
        await model.AddSubtitlesCommand.ExecuteAsync(null);

        _player.Received(1).AddSubtitleFile("C:\\films\\dune.srt");
        await _dialogs.Received(1).ShowErrorAsync("Субтитры не загружены", "нет доступа");
    }

    /// <summary>
    /// Changes reported by the player on its own thread are applied once through the dispatcher.
    /// </summary>
    [Fact]
    public void PlayerChanges_AreCoalesced()
    {
        var posted = new List<Action>();
        var dispatcher = Substitute.For<IUiDispatcher>();
        dispatcher.When(d => d.Post(Arg.Any<Action>())).Do(call => posted.Add(call.Arg<Action>()));
        var model = new TracksViewModel(_player, _settings, _dialogs, dispatcher, _features);
        _tracks = Film(audioSelected: 1, subtitleSelected: null);

        _player.TracksChanged += Raise.Event();
        _player.TracksChanged += Raise.Event();
        Assert.Single(posted);
        posted[0]();

        Assert.True(model.HasTracks);
        _player.TracksChanged += Raise.Event();
        Assert.Equal(2, posted.Count);
    }

    /// <summary>
    /// Without subtitles offered the list is empty and the default subtitles of the file are switched off, while the
    /// sound tracks stay.
    /// </summary>
    [Fact]
    public void Unavailable_SubtitlesAreHiddenAndOff()
    {
        _features.Set(AppOnlyFeature.Subtitles, false);
        _tracks = Film(audioSelected: 1, subtitleSelected: 1);
        var model = Create();

        model.Refresh();

        _player.Received(1).SelectTrack(MediaTrackKind.Subtitle, null);
        Assert.False(model.SubtitlesAvailable);
        Assert.Empty(model.SubtitleTracks);
        Assert.False(model.SubtitlesShown);
        Assert.Equal(2, model.AudioTracks.Count);
    }

    /// <summary>
    /// Without subtitles offered the remembered subtitle language is neither applied nor overwritten, while the sound
    /// language still is applied.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Unavailable_PreferenceIsKept()
    {
        _features.Set(AppOnlyFeature.Subtitles, false);
        _stored[TracksViewModel.SUBTITLE_LANGUAGE_SETTING] = "ru";
        _stored[TracksViewModel.AUDIO_LANGUAGE_SETTING] = "ru";
        _tracks = Film(audioSelected: 1, subtitleSelected: null);
        var model = Create();

        await model.LoadAsync();

        _player.DidNotReceive().SelectTrack(MediaTrackKind.Subtitle, 1);
        _player.Received(1).SelectTrack(MediaTrackKind.Audio, 2);
        await _settings.DidNotReceive().SetAsync(TracksViewModel.SUBTITLE_LANGUAGE_SETTING, Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal("ru", _stored[TracksViewModel.SUBTITLE_LANGUAGE_SETTING]);
    }

    /// <summary>
    /// Without subtitles offered a subtitle file is not even asked for; a sound file still is.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Unavailable_AddFileDoesNothing()
    {
        _features.Set(AppOnlyFeature.Subtitles, false);
        var model = Create();

        await model.AddSubtitlesCommand.ExecuteAsync(null);
        await model.AddAudioCommand.ExecuteAsync(null);

        await _dialogs.DidNotReceive().PickSubtitleFileAsync();
        await _dialogs.Received(1).PickAudioFileAsync();
    }

    /// <summary>
    /// Without subtitles offered a subtitle track that appears later and is selected by the player is switched off too.
    /// </summary>
    [Fact]
    public void Unavailable_LaterSubtitlesAreSwitchedOff()
    {
        _features.Set(AppOnlyFeature.Subtitles, false);
        _tracks = Film(audioSelected: 1, subtitleSelected: null);
        var model = Create();
        model.Refresh();

        _tracks = [.. _tracks, new MediaTrack(3, MediaTrackKind.Subtitle, "dune.srt", null, "subrip", null, false, true, true)];
        model.Refresh();

        _player.Received(1).SelectTrack(MediaTrackKind.Subtitle, null);
        Assert.False(model.SubtitlesShown);
    }

    /// <summary>
    /// Subtitles offered in the middle of a film show the subtitle list and applies the remembered language at once.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Opened_PreferenceApplies()
    {
        _features.Set(AppOnlyFeature.Subtitles, false);
        _stored[TracksViewModel.SUBTITLE_LANGUAGE_SETTING] = "ru";
        _tracks = Film(audioSelected: 1, subtitleSelected: null);
        var model = Create();
        await model.LoadAsync();

        _features.Set(AppOnlyFeature.Subtitles, true);

        _player.Received(1).SelectTrack(MediaTrackKind.Subtitle, 1);
        Assert.True(model.SubtitlesAvailable);
        Assert.Equal(3, model.SubtitleTracks.Count);
        Assert.True(model.SubtitlesShown);
    }

    /// <summary>
    /// Subtitles offered while the model is being created (on another thread) are not lost.
    /// </summary>
    [Fact]
    public void OpenedDuringCreation_IsNotLost()
    {
        var dispatcher = Substitute.For<IUiDispatcher>();
        dispatcher.When(d => d.Post(Arg.Any<Action>())).Do(call => call.Arg<Action>()());

        var model = new TracksViewModel(_player, _settings, _dialogs, dispatcher, new OpenedOnSubscribe());

        Assert.True(model.SubtitlesAvailable);
    }

    /// <summary>
    /// Builds the tracks of a film with Russian and English sound and subtitles.
    /// </summary>
    /// <param name="audioSelected">The selected sound track.</param>
    /// <param name="subtitleSelected">The selected subtitles.</param>
    /// <returns>The tracks.</returns>
    private static List<MediaTrack> Film(long audioSelected, long? subtitleSelected) =>
    [
        new MediaTrack(1, MediaTrackKind.Audio, "Original", "eng", "dts", 6, true, false, audioSelected == 1),
        new MediaTrack(2, MediaTrackKind.Audio, "Дубляж", "rus", "ac3", 6, false, false, audioSelected == 2),
        new MediaTrack(1, MediaTrackKind.Subtitle, null, "rus", "subrip", null, true, false, subtitleSelected == 1),
        new MediaTrack(2, MediaTrackKind.Subtitle, null, "eng", "subrip", null, false, false, subtitleSelected == 2),
    ];

    /// <summary>
    /// Creates the model with an immediate dispatcher.
    /// </summary>
    /// <returns>The model.</returns>
    private TracksViewModel Create()
    {
        var dispatcher = Substitute.For<IUiDispatcher>();
        dispatcher.When(d => d.Post(Arg.Any<Action>())).Do(call => call.Arg<Action>()());
        return new TracksViewModel(_player, _settings, _dialogs, dispatcher, _features);
    }

    /// <summary>
    /// Access whose subtitles open just before the first listener subscribes, as a change on another thread at the
    /// start can.
    /// </summary>
    private sealed class OpenedOnSubscribe : IFeatureAccess
    {
        /// <summary>
        /// Whether subtitles are offered.
        /// </summary>
        private bool _opened;

        /// <summary>
        /// The listeners.
        /// </summary>
        private EventHandler? _changed;

        /// <inheritdoc />
        public event EventHandler? Changed
        {
            add
            {
                if (!_opened)
                {
                    _opened = true;
                    _changed?.Invoke(this, EventArgs.Empty);
                }

                _changed += value;
            }

            remove => _changed -= value;
        }

        /// <inheritdoc />
        public bool IsAvailable(AppOnlyFeature feature) => feature == AppOnlyFeature.Subtitles && _opened;
    }
}
