using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Golether.Browser.Enums;
using Golether.Browser.Interop;
using Golether.Browser.Media;
using Golether.Browser.Services;
using Golether.Browser.Session;
using Golether.Client.Enums;
using Golether.Client.Localization;
using Golether.Client.Services;
using Golether.Client.ViewModels;
using Golether.Core.Configuration;
using Golether.Core.Data.Enums;
using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Core.Session;
using Golether.Localization;
using Golether.Security.Invites;
using Golether.Session;
using Golether.Sync.Engine;
using Golether.Sync.Protocol;

namespace Golether.Browser.ViewModels;

/// <summary>
/// The page: the start screen and the session, laid out like the main window of the desktop application, with the
/// same shared parts (the chat, the strokes, the tracks, the volume, the tiles, the languages with their flags).
/// </summary>
internal sealed partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// The setting with the name of the user.
    /// </summary>
    private const string NAME_SETTING = "profile.displayName";

    /// <summary>
    /// The longest name of a user or a session.
    /// </summary>
    private const int MAX_NAME_LENGTH = 64;

    /// <summary>
    /// The number of lines the event feed keeps.
    /// </summary>
    private const int MAX_EVENTS = 50;

    /// <summary>
    /// How often the screen reads the session.
    /// </summary>
    private static readonly TimeSpan _refreshPeriod = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How often the pictures of the cameras are copied into the tiles.
    /// </summary>
    private static readonly TimeSpan _cameraPeriod = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// The step of the back and forward buttons.
    /// </summary>
    private static readonly TimeSpan _seekStep = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The rules that tell when a participant is ready, for the line about whom the start waits for.
    /// </summary>
    private static readonly PlaybackAuthorityOptions _readinessOptions = new();

    /// <summary>
    /// The page.
    /// </summary>
    private readonly BrowserApp _app;

    /// <summary>
    /// The language of the page.
    /// </summary>
    private readonly ILanguageService _languages;

    /// <summary>
    /// The errors the shared view models report.
    /// </summary>
    private readonly BrowserDialogs _dialogs = new();

    /// <summary>
    /// The avatars of the participants.
    /// </summary>
    private readonly PeerAvatars _avatars = new();

    /// <summary>
    /// The pictures of the cameras in the tiles.
    /// </summary>
    private readonly CameraPictures _cameras = new();

    /// <summary>
    /// The session messages of the page: they go to the current session, whichever it is.
    /// </summary>
    private readonly SessionMessages _messages;

    /// <summary>
    /// The clap when another participant writes in the chat.
    /// </summary>
    private readonly ChatSound _chatSound;

    /// <summary>
    /// Reads the session on a schedule.
    /// </summary>
    private readonly DispatcherTimer _refreshTimer;

    /// <summary>
    /// Copies the pictures of the cameras on a schedule.
    /// </summary>
    private readonly DispatcherTimer _cameraTimer;

    /// <summary>
    /// Cancels the wait for the host while joining.
    /// </summary>
    private CancellationTokenSource? _joining;

    /// <summary>
    /// The invitation the page was opened with or the user pasted, checked.
    /// </summary>
    private Invite? _invite;

    /// <summary>
    /// The chosen language.
    /// </summary>
    private LanguageOption? _selectedLanguage;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="app">The page.</param>
    /// <param name="languages">The language of the page.</param>
    public MainViewModel(BrowserApp app, ILanguageService languages)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _languages = languages ?? throw new ArgumentNullException(nameof(languages));

        var dispatcher = new BrowserDispatcher();
        var features = new BrowserFeatures();
        _messages = new SessionMessages(app);
        Chat = new ChatViewModel(_messages, dispatcher);
        _chatSound = new ChatSound(_messages, app.Settings, new BrowserSoundPlayer(), TimeProvider.System);
        Drawing = new DrawingViewModel(_messages, dispatcher, features);
        Tracks = new TracksViewModel(app.LocalControls, app.Settings, _dialogs, dispatcher, features);
        Volume = new VolumeViewModel(app.LocalControls, app.Settings);
        Devices = new DevicesViewModel(new BrowserDeviceSelector(app.Conference), app.Settings);
        Languages = [.. languages.Languages.Select(l => new LanguageOption(l, FlagImages.Load("Golether.Browser", l.Code)))];
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == languages.Current.Code);

        _dialogs.ErrorShown += (_, error) => Dispatcher.UIThread.Post(() => ShowError(error.Title, error.Message));
        app.Prompt.Asked += (_, question) => Dispatcher.UIThread.Post(() => Question = question);
        app.Prompt.Withdrawn += (_, id) => Dispatcher.UIThread.Post(() => Withdraw(id));

        _refreshTimer = new DispatcherTimer { Interval = _refreshPeriod };
        _refreshTimer.Tick += (_, _) => Refresh();
        _cameraTimer = new DispatcherTimer { Interval = _cameraPeriod };
        _cameraTimer.Tick += (_, _) => _cameras.Update(Participants);
    }

    /// <summary>
    /// Gets the chat and the reactions.
    /// </summary>
    public ChatViewModel Chat { get; }

    /// <summary>
    /// Gets the strokes over the film; drawing belongs to the applications.
    /// </summary>
    public DrawingViewModel Drawing { get; }

    /// <summary>
    /// Gets the sound tracks of the film on this page.
    /// </summary>
    public TracksViewModel Tracks { get; }

    /// <summary>
    /// Gets the volume of the film on this page.
    /// </summary>
    public VolumeViewModel Volume { get; }

    /// <summary>
    /// Gets the choice of the camera and the microphone.
    /// </summary>
    public DevicesViewModel Devices { get; }

    /// <summary>
    /// Gets the languages with their flags.
    /// </summary>
    public IReadOnlyList<LanguageOption> Languages { get; }

    /// <summary>
    /// Gets the participants of the session.
    /// </summary>
    public ObservableCollection<ParticipantItemViewModel> Participants { get; } = [];

    /// <summary>
    /// Gets the event feed, the newest line first.
    /// </summary>
    public ObservableCollection<string> Events { get; } = [];

    /// <summary>
    /// Gets or sets the chosen language.
    /// </summary>
    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || !SetProperty(ref _selectedLanguage, value))
            {
                return;
            }

            _ = _languages.SelectAsync(value.Code, CancellationToken.None);
            RefreshTexts();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether a message of another participant sounds.
    /// </summary>
    public bool ChatSoundEnabled
    {
        get => _chatSound.Enabled;
        set
        {
            _chatSound.SetEnabled(value);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the name of the user.
    /// </summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of a new session.
    /// </summary>
    [ObservableProperty]
    public partial string NewSessionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the invitation link typed or pasted on the start screen.
    /// </summary>
    [ObservableProperty]
    public partial string InviteLink { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what the invitation leads to, or why it cannot be used here.
    /// </summary>
    [ObservableProperty]
    public partial string InviteDescription { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the invitation can be joined from a browser.
    /// </summary>
    [ObservableProperty]
    public partial bool CanJoin { get; set; }

    /// <summary>
    /// Gets or sets the failure shown on the start screen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHomeError))]
    public partial string HomeError { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether a failure is shown on the start screen.
    /// </summary>
    public bool HasHomeError => HomeError.Length > 0;

    /// <summary>
    /// Gets or sets a value indicating whether a session is open on the page.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowJoinScreen))]
    public partial bool IsInSession { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the page hosts the session.
    /// </summary>
    [ObservableProperty]
    public partial bool IsHost { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the page waits for the host to let it in.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowJoinScreen))]
    public partial bool IsJoining { get; set; }

    /// <summary>
    /// Gets a value indicating whether the screen of joining covers the page.
    /// </summary>
    public bool ShowJoinScreen => IsJoining;

    /// <summary>
    /// Gets or sets the title of the screen of joining.
    /// </summary>
    [ObservableProperty]
    public partial string JoinTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the text of the screen of joining.
    /// </summary>
    [ObservableProperty]
    public partial string JoinText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the host was reached and decides now.
    /// </summary>
    [ObservableProperty]
    public partial bool IsWaitingForHost { get; set; }

    /// <summary>
    /// Gets or sets the verification code of this page, read aloud to the host.
    /// </summary>
    [ObservableProperty]
    public partial string VerificationText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the start screen waits for a session to open.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Gets or sets the session name.
    /// </summary>
    [ObservableProperty]
    public partial string SessionTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name and the size of the film.
    /// </summary>
    [ObservableProperty]
    public partial string MediaCaption { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the state of the synchronization.
    /// </summary>
    [ObservableProperty]
    public partial string SyncText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how good the synchronization is.
    /// </summary>
    [ObservableProperty]
    public partial IndicatorLevel SyncLevel { get; set; }

    /// <summary>
    /// Gets or sets the line about the encryption and the number of participants.
    /// </summary>
    [ObservableProperty]
    public partial string SecurityText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether a film is chosen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilmOnScreen), nameof(ShowPlaceholder), nameof(CanPickFirstFile))]
    public partial bool HasMedia { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the session plays (this page is in).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilmOnScreen), nameof(ShowPlaceholder), nameof(CanPickFirstFile))]
    public partial bool IsActive { get; set; }

    /// <summary>
    /// Gets a value indicating whether the film and its controls are on the screen.
    /// </summary>
    public bool IsFilmOnScreen => HasMedia && IsActive;

    /// <summary>
    /// Gets a value indicating whether the text in place of the film is shown.
    /// </summary>
    public bool ShowPlaceholder => !IsFilmOnScreen;

    /// <summary>
    /// Gets a value indicating whether the host is offered to choose the first film.
    /// </summary>
    public bool CanPickFirstFile => IsHost && IsActive && !HasMedia;

    /// <summary>
    /// Gets or sets the text in place of the film.
    /// </summary>
    [ObservableProperty]
    public partial string VideoPlaceholder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the length of the film in seconds (at least 1, for the timeline).
    /// </summary>
    [ObservableProperty]
    public partial double DurationSeconds { get; set; } = 1;

    /// <summary>
    /// Gets or sets the position of the film in seconds.
    /// </summary>
    [ObservableProperty]
    public partial double PositionSeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user drags the timeline.
    /// </summary>
    [ObservableProperty]
    public partial bool IsScrubbing { get; set; }

    /// <summary>
    /// Gets or sets the position as text.
    /// </summary>
    [ObservableProperty]
    public partial string PositionText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the length as text.
    /// </summary>
    [ObservableProperty]
    public partial string DurationText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the film plays.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayIcon), nameof(ShowPauseIcon), nameof(PlayButtonText))]
    public partial bool IsPlaying { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the film reached its end.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayIcon), nameof(PlayButtonText))]
    public partial bool IsEnded { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the start waits for participants.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayIcon), nameof(ShowPauseIcon), nameof(PlayButtonText), nameof(CanStartNow))]
    public partial bool IsWaiting { get; set; }

    /// <summary>
    /// Gets a value indicating whether the play button shows the play sign.
    /// </summary>
    public bool ShowPlayIcon => !IsPlaying && !IsEnded && !IsWaiting;

    /// <summary>
    /// Gets a value indicating whether the play button shows the pause sign.
    /// </summary>
    public bool ShowPauseIcon => IsPlaying || IsWaiting;

    /// <summary>
    /// Gets what the play button does, for its hint.
    /// </summary>
    public string PlayButtonText => Texts.Get(IsEnded ? "Main.Play.Again" : IsWaiting ? "Main.Play.CancelStart" : IsPlaying ? "Main.Play.Pause" : "Main.Play.Play");

    /// <summary>
    /// Gets a value indicating whether the host may start without the participants that are not ready.
    /// </summary>
    public bool CanStartNow => IsHost && IsWaiting;

    /// <summary>
    /// Gets or sets the countdown or the line about whom the start waits for.
    /// </summary>
    [ObservableProperty]
    public partial string CountdownText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the seconds of the countdown alone, for a narrow bar.
    /// </summary>
    [ObservableProperty]
    public partial string CountdownShort { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the last line of the event feed, shown on the player bar.
    /// </summary>
    [ObservableProperty]
    public partial string LastEvent { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the shape of the picture, for the strokes.
    /// </summary>
    [ObservableProperty]
    public partial double VideoAspect { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the microphone of this page is off.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MicrophoneText))]
    public partial bool MicrophoneMuted { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the camera of this page is off.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CameraText))]
    public partial bool CameraOff { get; set; } = true;

    /// <summary>
    /// Gets the text of the microphone button.
    /// </summary>
    public string MicrophoneText => Texts.Get(MicrophoneMuted ? "Main.Microphone.Off" : "Main.Microphone.On");

    /// <summary>
    /// Gets the text of the camera button.
    /// </summary>
    public string CameraText => Texts.Get(CameraOff ? "Main.Camera.Off" : "Main.Camera.On");

    /// <summary>
    /// Gets or sets the open area of the participants column.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatTab), nameof(IsEventsTab), nameof(ShowChat), nameof(ShowEvents))]
    public partial SideTab Tab { get; set; } = SideTab.Chat;

    /// <summary>
    /// Gets a value indicating whether the chat tab is the open one.
    /// </summary>
    public bool IsChatTab => Tab == SideTab.Chat;

    /// <summary>
    /// Gets a value indicating whether the events tab is the open one.
    /// </summary>
    public bool IsEventsTab => Tab == SideTab.Events;

    /// <summary>
    /// Gets a value indicating whether the chat is shown.
    /// </summary>
    public bool ShowChat => Tab == SideTab.Chat;

    /// <summary>
    /// Gets a value indicating whether the event feed is shown.
    /// </summary>
    public bool ShowEvents => Tab == SideTab.Events;

    /// <summary>
    /// Gets or sets a value indicating whether the settings are open.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the invitation is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool IsInviteOpen { get; set; }

    /// <summary>
    /// Gets or sets the invitation link of the hosted session.
    /// </summary>
    [ObservableProperty]
    public partial string InviteText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what happened to the invitation last: copied, or made anew.
    /// </summary>
    [ObservableProperty]
    public partial string InviteNote { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the host is asked before ending the session.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLeaveConfirmOpen { get; set; }

    /// <summary>
    /// Gets or sets the guest the host is asked about, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuestion), nameof(QuestionTitle), nameof(QuestionCode))]
    public partial AdmissionQuestion? Question { get; set; }

    /// <summary>
    /// Gets a value indicating whether the host is asked about a guest.
    /// </summary>
    public bool HasQuestion => Question is not null;

    /// <summary>
    /// Gets the question about the guest.
    /// </summary>
    public string QuestionTitle => Question is { } question ? Texts.Format("Admission.Title", question.Name) : string.Empty;

    /// <summary>
    /// Gets the verification code of the guest.
    /// </summary>
    public string QuestionCode => Question?.Code ?? string.Empty;

    /// <summary>
    /// Gets or sets the title of the error shown over the page.
    /// </summary>
    [ObservableProperty]
    public partial string ErrorTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the error shown over the page.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether an error is shown over the page.
    /// </summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>
    /// Fills the start screen: the stored name and the invitation the page was opened with.
    /// </summary>
    public void Start()
    {
        DisplayName = _app.Settings.Get(NAME_SETTING) ?? string.Empty;
        NewSessionName = Texts.Get("Main.DefaultSessionName");
        _ = Volume.LoadAsync();
        _ = Tracks.LoadAsync();
        _ = LoadChatSoundAsync();

        if (PageInterop.TakeInvitation() is { Length: > 0 } payload)
        {
            InviteLink = ProjectSite.WATCH_URL_PREFIX + payload;
        }

        _app.Conference.MuteChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
        _refreshTimer.Start();
    }

    /// <summary>
    /// Stores the name and checks it.
    /// </summary>
    /// <param name="value">The new name.</param>
    partial void OnDisplayNameChanged(string value)
        => _app.Settings.Set(NAME_SETTING, value.Trim());

    /// <summary>
    /// Checks a pasted invitation and tells what it leads to.
    /// </summary>
    /// <param name="value">The link.</param>
    partial void OnInviteLinkChanged(string value) => DescribeInvitation();

    /// <summary>
    /// Opens a session on this page.
    /// </summary>
    /// <returns>A task that completes when the session is open or failed.</returns>
    [RelayCommand]
    private async Task StartHostingAsync()
    {
        ConferenceInterop.Prepare(true, true);
        await LeaveNowAsync().ConfigureAwait(true);
        await OpenAsync(async () =>
        {
            var session = await BrowserSession.HostAsync(_app, Trimmed(NewSessionName, Texts.Get("Main.DefaultSessionName")), Trimmed(DisplayName, string.Empty)).ConfigureAwait(true);
            Attach(session);
        }, busy: true);
    }

    /// <summary>
    /// Joins the session of the invitation.
    /// </summary>
    /// <returns>A task that completes when this page is in, or the join failed.</returns>
    [RelayCommand]
    private async Task JoinAsync()
    {
        if (_invite is not { } invite || !CanJoin)
        {
            return;
        }

        ConferenceInterop.Prepare(true, true);
        await LeaveNowAsync().ConfigureAwait(true);
        IsJoining = true;
        ShowJoinTexts();
        _joining = new CancellationTokenSource();
        var cancellation = _joining.Token;
        await OpenAsync(async () =>
        {
            var session = BrowserSession.CreateGuest(_app, Trimmed(DisplayName, string.Empty));
            Attach(session);
            await session.JoinAsync(invite, cancellation).ConfigureAwait(true);
        }, busy: false);
        IsJoining = false;
        InviteLink = string.Empty;
    }

    /// <summary>
    /// Stops waiting for the host.
    /// </summary>
    /// <returns>A task that completes when the page left.</returns>
    [RelayCommand]
    private async Task CancelJoinAsync()
    {
        _joining?.Cancel();
        await LeaveNowAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Puts the text on the clipboard into the invitation field.
    /// </summary>
    /// <returns>A task that completes when the text was read.</returns>
    [RelayCommand]
    private async Task PasteInviteAsync()
    {
        if (await PageInterop.PasteAsync().ConfigureAwait(true) is { Length: > 0 } text)
        {
            InviteLink = text.Trim();
        }
    }

    /// <summary>
    /// Chooses the film to show (host).
    /// </summary>
    /// <returns>A task that completes when the film is shared or the user chose none.</returns>
    [RelayCommand]
    private async Task PickFileAsync()
    {
        if (_app.Session is not { IsHost: true } session)
        {
            return;
        }

        var file = await FileInterop.PickAsync().ConfigureAwait(true);
        if (file <= 0)
        {
            return;
        }

        try
        {
            await session.ShareAsync(BrowserFiles.Add(file, FileInterop.Name(file), (long)FileInterop.Size(file))).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            ShowError(Texts.Get("Main.Error.CommandNotSent"), ex.Message);
        }
    }

    /// <summary>
    /// Starts or pauses playback for everybody.
    /// </summary>
    /// <returns>A task that completes when the intent was sent.</returns>
    [RelayCommand]
    private Task TogglePlayAsync()
        => SendAsync(IsEnded
            ? new PlaybackRequest(PlaybackRequestKind.Play, TimeSpan.Zero)
            : IsWaiting
                ? new PlaybackRequest(PlaybackRequestKind.Pause, TimeSpan.FromSeconds(PositionSeconds))
                : new PlaybackRequest(IsPlaying ? PlaybackRequestKind.Pause : PlaybackRequestKind.Play, IsPlaying ? TimeSpan.FromSeconds(PositionSeconds) : null));

    /// <summary>
    /// Starts playback without waiting for participants that are not ready (host).
    /// </summary>
    /// <returns>A task that completes when the intent was sent.</returns>
    [RelayCommand]
    private Task StartNowAsync() => SendAsync(new PlaybackRequest(PlaybackRequestKind.PlayNow, null));

    /// <summary>
    /// Seeks back by ten seconds.
    /// </summary>
    /// <returns>A task that completes when the intent was sent.</returns>
    [RelayCommand]
    private Task SeekBackAsync() => SeekToAsync(Math.Max(0, PositionSeconds - _seekStep.TotalSeconds));

    /// <summary>
    /// Seeks forward by ten seconds.
    /// </summary>
    /// <returns>A task that completes when the intent was sent.</returns>
    [RelayCommand]
    private Task SeekForwardAsync() => SeekToAsync(Math.Min(DurationSeconds, PositionSeconds + _seekStep.TotalSeconds));

    /// <summary>
    /// Seeks everybody to a position.
    /// </summary>
    /// <param name="seconds">The position in seconds.</param>
    /// <returns>A task that completes when the intent was sent.</returns>
    [RelayCommand]
    private Task SeekToAsync(double seconds)
    {
        PositionSeconds = seconds;
        return SendAsync(new PlaybackRequest(PlaybackRequestKind.Seek, TimeSpan.FromSeconds(seconds)));
    }

    /// <summary>
    /// Switches the microphone of this page.
    /// </summary>
    [RelayCommand]
    private void ToggleMicrophone()
    {
        if (MicrophoneMuted)
        {
            ConferenceInterop.Prepare(false, true);
        }

        _app.Session?.SetDevices(MicrophoneMuted, !CameraOff);
        Refresh();
    }

    /// <summary>
    /// Switches the camera of this page.
    /// </summary>
    [RelayCommand]
    private void ToggleCamera()
    {
        if (CameraOff)
        {
            ConferenceInterop.Prepare(true, false);
        }

        _app.Session?.SetDevices(!MicrophoneMuted, CameraOff);
        Refresh();
    }

    /// <summary>
    /// Opens an area of the participants column; a click on the open one folds it.
    /// </summary>
    /// <param name="tab">The area.</param>
    [RelayCommand]
    private void ShowTab(SideTab tab)
    {
        Tab = Tab == tab ? SideTab.None : tab;

        // The chat counts what came while it was out of sight: its tab shows the mark until it is opened.
        Chat.IsExpanded = Tab == SideTab.Chat;
    }

    /// <summary>
    /// Opens the settings.
    /// </summary>
    /// <returns>A task that completes when the devices are listed.</returns>
    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        IsSettingsOpen = true;
        await Devices.RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Closes the settings.
    /// </summary>
    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    /// <summary>
    /// Shows the invitation of the hosted session.
    /// </summary>
    [RelayCommand]
    private void OpenInvite() => ShowInvite(false);

    /// <summary>
    /// Makes a new invitation instead of the shown one.
    /// </summary>
    [RelayCommand]
    private void RenewInvite() => ShowInvite(true);

    /// <summary>
    /// Copies the invitation link.
    /// </summary>
    /// <returns>A task that completes when the link is on the clipboard.</returns>
    [RelayCommand]
    private async Task CopyInviteAsync()
    {
        try
        {
            await PageInterop.CopyAsync(InviteText).ConfigureAwait(true);
            InviteNote = Texts.Get("Invite.Dialog.Copied");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.JavaScript.JSException or InvalidOperationException)
        {
            InviteNote = ex.Message;
        }
    }

    /// <summary>
    /// Closes the invitation.
    /// </summary>
    [RelayCommand]
    private void CloseInvite() => IsInviteOpen = false;

    /// <summary>
    /// Leaves the session; the host is asked first, since the session ends for everybody.
    /// </summary>
    /// <returns>A task that completes when the page left or the host was asked.</returns>
    [RelayCommand]
    private async Task LeaveAsync()
    {
        if (IsHost && IsActive)
        {
            IsLeaveConfirmOpen = true;
            return;
        }

        await LeaveNowAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Ends the session after the host agreed.
    /// </summary>
    /// <returns>A task that completes when the page left.</returns>
    [RelayCommand]
    private async Task ConfirmLeaveAsync()
    {
        IsLeaveConfirmOpen = false;
        await LeaveNowAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Keeps the session after all.
    /// </summary>
    [RelayCommand]
    private void CancelLeave() => IsLeaveConfirmOpen = false;

    /// <summary>
    /// Lets the guest in.
    /// </summary>
    [RelayCommand]
    private void Admit() => Answer(true);

    /// <summary>
    /// Turns the guest away.
    /// </summary>
    [RelayCommand]
    private void Reject() => Answer(false);

    /// <summary>
    /// Closes the error shown over the page.
    /// </summary>
    [RelayCommand]
    private void CloseError() => ErrorText = string.Empty;

    /// <summary>
    /// Toggles the full screen of the page.
    /// </summary>
    [RelayCommand]
    private void ToggleFullScreen() => PageInterop.ToggleFullScreen();

    /// <summary>
    /// Opens a session and shows the failure on the start screen when it did not open.
    /// </summary>
    /// <param name="open">The opening.</param>
    /// <param name="busy">Whether the start screen shows that it waits (a join has a screen of its own).</param>
    /// <returns>A task that completes when the session is open or the failure is shown.</returns>
    private async Task OpenAsync(Func<Task> open, bool busy)
    {
        IsBusy = busy;
        HomeError = string.Empty;
        try
        {
            await open().ConfigureAwait(true);
            PageInterop.KeepAwake(true);
        }
        catch (OperationCanceledException)
        {
            await LeaveNowAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException or TimeoutException or System.Runtime.InteropServices.JavaScript.JSException)
        {
            _app.Log.LogWarning("The session did not open: {Reason}", ex.Message);
            await LeaveNowAsync().ConfigureAwait(true);
            HomeError = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    /// <summary>
    /// Makes a session the session of the page.
    /// </summary>
    /// <param name="session">The session.</param>
    private void Attach(BrowserSession session)
    {
        _app.Session = session;
        session.Changed += (_, _) => Dispatcher.UIThread.Post(Refresh);
        session.EventRaised += (_, e) => Dispatcher.UIThread.Post(() => AddEvent(e.Text));
        session.AvatarReceived += (_, update) => Dispatcher.UIThread.Post(() => _avatars.Apply(update));
        _messages.Attach(session);
        IsInSession = true;
        IsHost = session.IsHost;
        _cameraTimer.Start();
        Refresh();
    }

    /// <summary>
    /// Leaves the session of the page, if any, and clears the screen.
    /// </summary>
    /// <returns>A task that completes when the session is over.</returns>
    private async Task LeaveNowAsync()
    {
        _cameraTimer.Stop();
        if (_app.Session is { } session)
        {
            _app.Session = null;
            _messages.Detach();
            await session.DisposeAsync().ConfigureAwait(true);
            ConferenceInterop.StopAll();
            FileInterop.Clear();
        }

        IsInSession = false;
        IsHost = false;
        IsJoining = false;
        IsActive = false;
        HasMedia = false;
        Question = null;
        IsInviteOpen = false;
        Participants.Clear();
        _cameras.Clear();
        _avatars.Clear();
        Events.Clear();
        LastEvent = string.Empty;
        Chat.Reset();
        Drawing.Reset();
        PageInterop.KeepAwake(false);
    }

    /// <summary>
    /// Reads the session and updates the screen.
    /// </summary>
    private void Refresh()
    {
        Chat.Tick();
        Drawing.Tick();
        if (_app.Session is not { } session)
        {
            return;
        }

        var snapshot = session.GetSnapshot();
        IsHost = snapshot.IsHost;
        IsActive = snapshot.State == SessionState.Active;
        SessionTitle = snapshot.SessionName;
        IsWaitingForHost = snapshot.State == SessionState.AwaitingApproval;
        VerificationText = snapshot.VerificationCode?.ToString() ?? string.Empty;
        if (IsJoining)
        {
            ShowJoinTexts();
        }

        HasMedia = snapshot.Media is not null;
        MediaCaption = snapshot.Media is { } media
            ? $"{media.FileName} · {DisplayFormat.Size(media.Length)}"
            : Texts.Get(snapshot.IsHost ? "Main.Caption.ChooseFile" : "Main.Placeholder.HostNotChosen");
        VideoPlaceholder = Placeholder(snapshot);

        UpdatePlayback(snapshot);
        UpdateParticipants(snapshot, snapshot.Local?.Snapshot.Duration);
        UpdateSyncChip(snapshot);
        SecurityText = Texts.Format("Main.Security", Participants.Count, SessionOptions.MAX_PARTICIPANTS_LIMIT);
        MicrophoneMuted = session.MicrophoneMuted;
        CameraOff = session.CameraOff;
        Drawing.CanDraw = false;
        VideoAspect = _app.LocalControls.GetVideoAspect();

        if (snapshot.State == SessionState.Ended && !IsJoining)
        {
            AddEvent(Texts.Get(snapshot.IsHost ? "Browser.Session.Ended" : "Session.Error.HostEnded"));
            _ = LeaveNowAsync();
        }
    }

    /// <summary>
    /// Updates the player bar.
    /// </summary>
    /// <param name="snapshot">The session.</param>
    private void UpdatePlayback(SessionSnapshot snapshot)
    {
        var local = snapshot.Local;
        var duration = local?.Snapshot.Duration;
        DurationSeconds = Math.Max(1, duration?.TotalSeconds ?? 1);
        DurationText = DisplayFormat.Position(duration);
        var position = local?.Snapshot.Position ?? local?.Expected;
        var seeking = local?.Snapshot is { IsLoaded: true, IsBuffering: true };
        if (!IsScrubbing)
        {
            var shown = seeking ? local?.Expected ?? position : position;
            PositionSeconds = duration is { Ticks: > 0 } ? Math.Clamp(shown?.TotalSeconds ?? 0, 0, DurationSeconds) : 0;
        }

        PositionText = DisplayFormat.Position(position);
        IsPlaying = snapshot.Playback?.State == PlayState.Playing;
        IsEnded = snapshot.Playback is { State: PlayState.Paused, Cause: PlaybackCause.Ended };
        IsWaiting = snapshot.Playback is { State: PlayState.Paused, Cause: PlaybackCause.WaitingForParticipants };
        var seconds = !IsWaiting && local?.StartsIn is { } startsIn ? Math.Ceiling(startsIn.TotalSeconds).ToString("0", CultureInfo.InvariantCulture) : null;
        CountdownText = IsWaiting
            ? DescribeWaiting(snapshot, duration)
            : seconds is not null ? Texts.Format("Main.StartsIn", seconds) : string.Empty;
        CountdownShort = seconds ?? string.Empty;
    }

    /// <summary>
    /// Updates the tiles of the participants.
    /// </summary>
    /// <param name="snapshot">The session.</param>
    /// <param name="duration">The length of the film.</param>
    private void UpdateParticipants(SessionSnapshot snapshot, TimeSpan? duration)
    {
        var views = snapshot.Participants;
        for (var i = Participants.Count - 1; i >= 0; i--)
        {
            if (views.All(v => v.Info.PeerId != Participants[i].PeerId))
            {
                Participants.RemoveAt(i);
            }
        }

        for (var i = 0; i < views.Count; i++)
        {
            var item = Participants.FirstOrDefault(p => p.PeerId == views[i].Info.PeerId);
            if (item is null)
            {
                item = new ParticipantItemViewModel(views[i].Info.PeerId, SwitchOffAsync, ChangeVoiceVolume, RemoveAsync);
                Participants.Insert(Math.Min(i, Participants.Count), item);
            }

            item.Update(views[i], duration);
            item.AvatarImage = _avatars.Find(item.PeerId);
            item.CanModerate = IsHost && !item.IsLocal;
            var speaking = _app.Session?.IsSpeaking(views[i].IsLocal ? default : item.PeerId) == true;
            item.IsSpeaking = speaking && !(item.IsLocal ? MicrophoneMuted : item.MicrophoneOff);
            if (item.IsLocal)
            {
                item.CameraOff = CameraOff;
                item.MicrophoneOff = MicrophoneMuted;
            }
        }
    }

    /// <summary>
    /// Updates the synchronization chip, as on the desktop.
    /// </summary>
    /// <param name="snapshot">The session.</param>
    private void UpdateSyncChip(SessionSnapshot snapshot)
    {
        if (snapshot.State is SessionState.Connecting or SessionState.AwaitingApproval)
        {
            SyncText = Texts.Get("Main.Sync.WaitingHost");
            SyncLevel = IndicatorLevel.Warning;
            return;
        }

        var drifts = snapshot.Participants.Select(p => p.Status).OfType<ParticipantStatus>().Where(s => s.Position is not null).ToArray();
        if (drifts.Length == 0 || snapshot.Playback?.State != PlayState.Playing)
        {
            SyncText = Texts.Get(snapshot.Playback?.State == PlayState.Playing ? "Main.Sync.Syncing" : IsEnded ? "Main.Sync.Ended" : "Main.Sync.Paused");
            SyncLevel = IndicatorLevel.Neutral;
            return;
        }

        var worst = drifts.Max(s => s.Drift.Duration());
        SyncText = Texts.Format("Main.Sync.InSync", Texts.Format("Format.Milliseconds", (int)worst.TotalMilliseconds));
        SyncLevel = DisplayFormat.DriftLevel(worst);
        if (drifts.Any(s => s.IsBuffering))
        {
            SyncText = Texts.Get("Main.Sync.SomeoneBuffering");
            SyncLevel = IndicatorLevel.Warning;
        }
    }

    /// <summary>
    /// Names the participants playback waits for.
    /// </summary>
    /// <param name="snapshot">The session.</param>
    /// <param name="duration">The length of the film.</param>
    /// <returns>The text, for example <c>Waiting for: Marina and Oleg…</c>.</returns>
    private static string DescribeWaiting(SessionSnapshot snapshot, TimeSpan? duration)
    {
        var target = snapshot.Playback?.Position ?? TimeSpan.Zero;
        var names = snapshot.Participants
            .Where(p => !p.Info.IsHost && (p.Status is not { } status ||
                !_readinessOptions.IsReady(status, duration) ||
                status.Position is not { } position ||
                (position - target).Duration() > _readinessOptions.WaitThreshold))
            .Select(p => p.Info.DisplayName)
            .ToArray();
        return names.Length switch
        {
            0 => Texts.Get("Main.Waiting.Everybody"),
            1 => Texts.Format("Main.Waiting.One", names[0]),
            2 => Texts.Format("Main.Waiting.Two", names[0], names[1]),
            _ => Texts.Format("Main.Waiting.Many", names[0], names[1], names.Length - 2),
        };
    }

    /// <summary>
    /// Gives the text in place of the film.
    /// </summary>
    /// <param name="snapshot">The session.</param>
    /// <returns>The text.</returns>
    private static string Placeholder(SessionSnapshot snapshot) => snapshot.State switch
    {
        SessionState.Connecting => Texts.Get("Main.Busy.Connecting"),
        SessionState.AwaitingApproval => Texts.Get("Main.Approval.Title"),
        SessionState.Ended => Texts.Get(snapshot.IsHost ? "Browser.Session.Ended" : "Session.Error.HostEnded"),
        _ => Texts.Get(snapshot.IsHost ? "Main.Placeholder.ChooseFile" : "Main.Placeholder.HostNotChosen"),
    };

    /// <summary>
    /// Sends an intention about the playback and reports a failure in the event feed.
    /// </summary>
    /// <param name="request">The intention.</param>
    /// <returns>A task that completes when the intention was sent.</returns>
    private async Task SendAsync(PlaybackRequest request)
    {
        if (_app.Session is not { } session)
        {
            return;
        }

        try
        {
            await session.RequestAsync(request).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            AddEvent(Texts.Get("Main.Error.CommandNotSent") + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Switches devices of a guest off (host).
    /// </summary>
    /// <param name="peer">The guest.</param>
    /// <param name="microphone">Whether to switch the microphone off.</param>
    /// <param name="camera">Whether to switch the camera off.</param>
    /// <returns>A task that completes when the guest was told.</returns>
    private async Task SwitchOffAsync(PeerId peer, bool microphone, bool camera)
    {
        if (_app.Session is not { IsHost: true } session)
        {
            return;
        }

        try
        {
            await session.SwitchOffAsync(peer, microphone, camera).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            ShowError(Texts.Get("Main.Error.SwitchOffFailed"), ex.Message);
        }
    }

    /// <summary>
    /// Removes a guest from the session (host).
    /// </summary>
    /// <param name="peer">The guest.</param>
    /// <returns>A task that completes when the guest was removed.</returns>
    private async Task RemoveAsync(PeerId peer)
    {
        if (_app.Session is not { IsHost: true } session)
        {
            return;
        }

        try
        {
            await session.RemoveAsync(peer).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            ShowError(Texts.Get("Main.Moderation.Remove"), ex.Message);
        }
    }

    /// <summary>
    /// Sets how loud a participant is heard on this page.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="percent">The volume, 0–100.</param>
    private void ChangeVoiceVolume(PeerId peer, double percent) => _app.Session?.SetVoiceVolume(peer, percent);

    /// <summary>
    /// Shows the invitation of the hosted session.
    /// </summary>
    /// <param name="renew">Whether to make a new one.</param>
    private void ShowInvite(bool renew)
    {
        if (_app.Session is not { IsHost: true } session)
        {
            return;
        }

        InviteText = PageInterop.Address() + "#" + session.Invite(renew);
        InviteNote = renew ? Texts.Get("Invite.Dialog.Created") : string.Empty;
        IsInviteOpen = true;
    }

    /// <summary>
    /// Checks the invitation of the start screen and tells what it leads to.
    /// </summary>
    private void DescribeInvitation()
    {
        _invite = null;
        CanJoin = false;
        InviteDescription = string.Empty;
        var text = InviteLink.Trim();
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            var invite = Security.Invites.Invite.ParseLink(text);
            if (invite.ExpiresAt <= TimeProvider.System.GetUtcNow())
            {
                InviteDescription = Texts.Get("Session.Error.InviteExpired");
                return;
            }

            if (invite.Signal is null)
            {
                InviteDescription = Texts.Get(invite.Rendezvous is null ? "Browser.Join.DirectOnly" : "Browser.Join.PrivateServer");
                return;
            }

            _invite = invite;
            CanJoin = true;
            InviteDescription = Texts.Format("Main.Link.Join.Text", invite.SessionName, invite.HostName);
        }
        catch (FormatException ex)
        {
            InviteDescription = Texts.Get("Main.Link.Bad.Title") + ": " + ex.Message;
        }
    }

    /// <summary>
    /// Answers the question about a guest.
    /// </summary>
    /// <param name="approve">Whether to let the guest in.</param>
    private void Answer(bool approve)
    {
        if (Question is { } question)
        {
            _app.Prompt.Answer(question.Id, approve);
            Question = null;
        }
    }

    /// <summary>
    /// Takes a question back.
    /// </summary>
    /// <param name="id">The number of the question.</param>
    private void Withdraw(int id)
    {
        if (Question?.Id == id)
        {
            Question = null;
        }
    }

    /// <summary>
    /// Reads whether the chat sounds and shows the choice.
    /// </summary>
    /// <returns>A task that completes when the setting is read.</returns>
    private async Task LoadChatSoundAsync()
    {
        await _chatSound.LoadAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(ChatSoundEnabled));
    }

    /// <summary>
    /// Fills the screen of joining as the desktop does: looking for the host, then waiting for its decision.
    /// </summary>
    private void ShowJoinTexts()
    {
        JoinTitle = Texts.Get(IsWaitingForHost ? "Main.Approval.Title" : "Main.Joining.Title");
        JoinText = Texts.Get(IsWaitingForHost ? "Main.Approval.Text" : "Main.Joining.Text");
    }

    /// <summary>
    /// Adds a line to the event feed.
    /// </summary>
    /// <param name="text">The line.</param>
    private void AddEvent(string text)
    {
        var line = TimeProvider.System.GetLocalNow().ToString("HH:mm", CultureInfo.InvariantCulture) + "  " + text;
        Events.Insert(0, line);
        while (Events.Count > MAX_EVENTS)
        {
            Events.RemoveAt(Events.Count - 1);
        }

        LastEvent = text;
    }

    /// <summary>
    /// Shows an error over the page.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The text.</param>
    private void ShowError(string title, string message)
    {
        ErrorTitle = title;
        ErrorText = message;
    }

    /// <summary>
    /// Updates the texts that do not follow the language by themselves.
    /// </summary>
    private void RefreshTexts()
    {
        OnPropertyChanged(nameof(MicrophoneText));
        OnPropertyChanged(nameof(CameraText));
        OnPropertyChanged(nameof(PlayButtonText));
        OnPropertyChanged(nameof(QuestionTitle));
        Chat.RefreshTexts();
        Volume.RefreshTexts();
        foreach (var participant in Participants)
        {
            participant.RefreshTexts();
        }

        DescribeInvitation();
        Refresh();
    }

    /// <summary>
    /// Gives a trimmed name, or a default one when it is empty.
    /// </summary>
    /// <param name="value">The typed name.</param>
    /// <param name="fallback">The name used when nothing was typed.</param>
    /// <returns>The name, at most <see cref="MAX_NAME_LENGTH"/> characters.</returns>
    private static string Trimmed(string value, string fallback)
    {
        var text = value.Trim();
        text = text.Length == 0 ? fallback : text;
        return text.Length > MAX_NAME_LENGTH ? text[..MAX_NAME_LENGTH] : text;
    }
}
