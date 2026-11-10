using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Golether.Browser.Interop;
using Golether.Browser.ViewModels;
using Golether.UI.Controls;
using Golether.UI.Enums;

namespace Golether.Browser.Views;

/// <summary>
/// The page. What the bindings cannot do is here, as in the main window of the desktop: the timeline that seeks when
/// released, a click on the film that plays or pauses, the volume scale that opens on hover, the keys, and the full
/// screen whose bars lie over the film and slide away with the cursor.
/// </summary>
public sealed partial class MainView : UserControl
{
    /// <summary>
    /// How close to the top or the bottom edge the pointer brings the hidden bars back, in pixels.
    /// </summary>
    private const double REVEAL_EDGE = 24;

    /// <summary>
    /// The page is "compact" below this width: a phone held upright gets one column and thinner bars.
    /// </summary>
    private const double COMPACT_WIDTH = 640;

    /// <summary>
    /// The page is "short" below this height: a phone on its side gets thinner bars and a scrolling column.
    /// </summary>
    private const double SHORT_HEIGHT = 540;

    /// <summary>
    /// An upright page narrower than this lays the participants under the film instead of beside it.
    /// </summary>
    private const double STACKED_MAX_WIDTH = 900;

    /// <summary>
    /// The width of the participants column beside the film, as on the desktop.
    /// </summary>
    private const double SIDE_WIDTH = 300;

    /// <summary>
    /// The height of the film per its width while the participants lie under it.
    /// </summary>
    private const double FILM_HEIGHT_PER_WIDTH = 9.0 / 16;

    /// <summary>
    /// The most of the height of the page the film takes while the participants lie under it.
    /// </summary>
    private const double FILM_MAX_SHARE = 0.5;

    /// <summary>
    /// The height the floating reactions rise in over a film that is high enough.
    /// </summary>
    private const double REACTIONS_HEIGHT = 360;

    /// <summary>
    /// The room the reactions keep from the top of the film, the same as their margin at its bottom.
    /// </summary>
    private const double REACTIONS_MARGIN = 18;

    /// <summary>
    /// The highest the list of tracks grows on a page that is high enough.
    /// </summary>
    private const double TRACKS_MAX_HEIGHT = 420;

    /// <summary>
    /// The height of the page the list of tracks leaves to the bars and the button it opens from.
    /// </summary>
    private const double TRACKS_ROOM = 140;

    /// <summary>
    /// The room kept above and below a field scrolled back into sight when the keyboard of a phone opens.
    /// </summary>
    private const double FOCUS_ROOM = 16;

    /// <summary>
    /// The layout the session has now; <see langword="null"/> until the page is first measured.
    /// </summary>
    private (bool Stacked, bool Short, bool FullScreen)? _layout;

    /// <summary>
    /// Whether the touch that has just come down only brings the hidden bars of the full screen back, so the tap that
    /// follows must not also play or pause the film.
    /// </summary>
    private bool _tapRevealsBars;

    /// <summary>
    /// Whether the cards of the start screen are arranged one under the other; <see langword="null"/> before the first
    /// arrangement.
    /// </summary>
    private bool? _startCompact;

    /// <summary>
    /// How long the bars and the cursor stay after the pointer stopped, as on the desktop.
    /// </summary>
    private static readonly TimeSpan _chromeDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How often the hiding of the bars and the cursor is looked at.
    /// </summary>
    private static readonly TimeSpan _chromePeriod = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Decides when the bars and the cursor hide, the same logic as on the desktop.
    /// </summary>
    private readonly ChromeAutoHide _chrome = new(TimeProvider.System, _chromeDelay);

    /// <summary>
    /// Lets the time of the hiding pass.
    /// </summary>
    private readonly DispatcherTimer _chromeTimer;

    /// <summary>
    /// The cursor over the film while it is hidden.
    /// </summary>
    private readonly Cursor _hiddenCursor = new(StandardCursorType.None);

    /// <summary>
    /// Whether the bars lie over the film now (full screen) instead of in the layout.
    /// </summary>
    private bool _barsOverlaid;

    /// <summary>
    /// Whether the page is in full screen.
    /// </summary>
    private bool _fullScreen;

    /// <summary>
    /// How long the volume scale stays after the pointer left the speaker.
    /// </summary>
    private static readonly TimeSpan _volumeCloseDelay = TimeSpan.FromMilliseconds(350);

    /// <summary>
    /// How long a click waits for a second click before it plays or pauses.
    /// </summary>
    private static readonly TimeSpan _doubleClickWindow = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Closes the volume scale a little after the pointer left.
    /// </summary>
    private readonly DispatcherTimer _volumeCloseTimer;

    /// <summary>
    /// Plays or pauses after a single click on the film.
    /// </summary>
    private readonly DispatcherTimer _clickTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainView"/> class.
    /// </summary>
    public MainView()
    {
        AvaloniaXamlLoader.Load(this);

        _volumeCloseTimer = new DispatcherTimer { Interval = _volumeCloseDelay };
        _volumeCloseTimer.Tick += (_, _) =>
        {
            _volumeCloseTimer.Stop();
            this.FindControl<Popup>("VolumePopup")!.IsOpen = false;
        };

        _clickTimer = new DispatcherTimer { Interval = _doubleClickWindow };
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            ViewModel?.TogglePlayCommand.Execute(null);
        };

        var timeline = this.FindControl<Control>("Timeline")!;
        timeline.AddHandler(PointerPressedEvent, OnTimelinePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        timeline.AddHandler(PointerReleasedEvent, OnTimelineReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is { } top)
            {
                top.AddHandler(PointerMovedEvent, OnPointerMovedAnywhere, RoutingStrategies.Tunnel, handledEventsToo: true);
                top.AddHandler(PointerPressedEvent, OnPointerPressedAnywhere, RoutingStrategies.Tunnel, handledEventsToo: true);
                top.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
                top.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
            }
        };
        PointerExited += (_, _) => ReportZone(PointerZone.Elsewhere);

        _chrome.Changed += (_, _) => ApplyChrome();
        _chromeTimer = new DispatcherTimer { Interval = _chromePeriod };
        _chromeTimer.Tick += (_, _) => UpdateChrome();
        _chromeTimer.Start();
        PageCallbacks.FullScreenChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _fullScreen = PageInterop.IsFullScreen();
            ApplyLayout(Bounds.Size);
            UpdateChrome();
        });

        var clickLayer = this.FindControl<Panel>("ClickLayer")!;
        clickLayer.Tapped += OnFilmTapped;
        clickLayer.DoubleTapped += OnFilmDoubleTapped;

        this.FindControl<Panel>("VideoArea")!.SizeChanged += (_, e) => FitReactions(e.NewSize.Height);
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyLayout(e.NewSize);

        // The keyboard of a phone makes the page lower. The field being typed in may now lie under its edge, and the
        // browser, which follows the hidden input of Avalonia at that field, pans the page and shows an empty strip.
        if (e.NewSize.Height < e.PreviousSize.Height)
        {
            Dispatcher.UIThread.Post(KeepFocusInView, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Scrolls the field that has the focus back into sight, with a little room around it, once the lower page is
    /// laid out.
    /// </summary>
    private void KeepFocusInView()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox field)
        {
            return;
        }

        field.BringIntoView(new Rect(0, -FOCUS_ROOM, field.Bounds.Width, field.Bounds.Height + (2 * FOCUS_ROOM)));
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>
    /// Stops the timeline from following the film while the user drags it.
    /// </summary>
    /// <param name="sender">The timeline.</param>
    /// <param name="e">The event data.</param>
    private void OnTimelinePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.IsScrubbing = true;
        }
    }

    /// <summary>
    /// Seeks everybody to the released position.
    /// </summary>
    /// <param name="sender">The timeline.</param>
    /// <param name="e">The event data.</param>
    private void OnTimelineReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (ViewModel is not { IsScrubbing: true } viewModel || sender is not Slider timeline)
        {
            return;
        }

        viewModel.IsScrubbing = false;
        viewModel.SeekToCommand.Execute(timeline.Value);
    }

    /// <summary>
    /// A click on the film plays or pauses once it is clear that no second click follows.
    /// </summary>
    /// <param name="sender">The film.</param>
    /// <param name="e">The event data.</param>
    private void OnFilmTapped(object? sender, TappedEventArgs e)
    {
        if (_tapRevealsBars)
        {
            _tapRevealsBars = false;
            return;
        }

        _clickTimer.Stop();
        _clickTimer.Start();
    }

    /// <summary>
    /// A double click on the film enters or leaves the full screen.
    /// </summary>
    /// <param name="sender">The film.</param>
    /// <param name="e">The event data.</param>
    private void OnFilmDoubleTapped(object? sender, TappedEventArgs e)
    {
        _clickTimer.Stop();
        ViewModel?.ToggleFullScreenCommand.Execute(null);
    }

    /// <summary>
    /// Closes the reactions once one was chosen. The flyout of a page lies in the same canvas: closed at once, it
    /// would take the button out of the tree before the button runs its command.
    /// </summary>
    /// <param name="sender">The reaction.</param>
    /// <param name="e">The event data.</param>
    private void OnReactionChosen(object? sender, RoutedEventArgs e) => HideFlyoutLater("ReactionsButton");

    /// <summary>
    /// Closes the tracks once one was chosen, after the button ran its command.
    /// </summary>
    /// <param name="sender">The track.</param>
    /// <param name="e">The event data.</param>
    private void OnTrackChosen(object? sender, RoutedEventArgs e) => HideFlyoutLater("TracksButton");

    /// <summary>
    /// Closes the flyout of a button once the click is handled.
    /// </summary>
    /// <param name="button">The name of the button.</param>
    private void HideFlyoutLater(string button)
        => Dispatcher.UIThread.Post(() => this.FindControl<Button>(button)?.Flyout?.Hide(), DispatcherPriority.Background);

    /// <summary>
    /// Opens the volume scale while the pointer is over the speaker or the scale.
    /// </summary>
    /// <param name="sender">The speaker or the scale.</param>
    /// <param name="e">The event data.</param>
    private void OnVolumePointerEntered(object? sender, PointerEventArgs e)
    {
        _volumeCloseTimer.Stop();
        if (ViewModel is { IsFilmOnScreen: true })
        {
            this.FindControl<Popup>("VolumePopup")!.IsOpen = true;
        }
    }

    /// <summary>
    /// Watches the pointer while the volume scale is open and closes the scale shortly after the pointer left both
    /// the speaker and the scale. A page lays the scale into the same canvas, so the speaker reports the pointer as
    /// gone the moment the scale opens; the position tells the truth.
    /// </summary>
    /// <param name="sender">The top level.</param>
    /// <param name="e">The event data.</param>
    private void OnPointerMovedAnywhere(object? sender, PointerEventArgs e)
    {
        ReportZone(ZoneAt(e.GetPosition(this)));
        if (this.FindControl<Popup>("VolumePopup") is not { IsOpen: true })
        {
            return;
        }

        if (IsOver(this.FindControl<Control>("VolumeButton"), e) || IsOver(this.FindControl<Control>("VolumePanel"), e))
        {
            _volumeCloseTimer.Stop();
        }
        else if (!_volumeCloseTimer.IsEnabled)
        {
            _volumeCloseTimer.Start();
        }
    }

    /// <summary>
    /// Lets a touch bring the bars back: a finger moves nothing until it comes down. In the full screen a tap on the
    /// film with the bars hidden only shows them, as a phone player does; the next tap plays or pauses. A press away
    /// from the speaker closes its volume scale.
    /// </summary>
    /// <param name="sender">The top level.</param>
    /// <param name="e">The event data.</param>
    private void OnPointerPressedAnywhere(object? sender, PointerPressedEventArgs e)
    {
        _tapRevealsBars = e.Pointer.Type == PointerType.Touch && _barsOverlaid && !_chrome.BarsVisible;
        ReportZone(ZoneAt(e.GetPosition(this)));

        // A finger does not leave the speaker the way a mouse does: a touch anywhere else closes its scale.
        if (this.FindControl<Popup>("VolumePopup") is { IsOpen: true } volume &&
            !IsOver(this.FindControl<Control>("VolumeButton"), e) &&
            !IsOver(this.FindControl<Control>("VolumePanel"), e))
        {
            _volumeCloseTimer.Stop();
            volume.IsOpen = false;
        }
    }

    /// <summary>
    /// Tells whether the pointer is over a control.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="e">The event data.</param>
    /// <returns><see langword="true"/> when the pointer is within the bounds of the control.</returns>
    private static bool IsOver(Control? control, PointerEventArgs e)
        => control is { IsEffectivelyVisible: true } && new Rect(control.Bounds.Size).Contains(e.GetPosition(control));

    /// <summary>
    /// Changes the volume with the wheel over the speaker or the scale.
    /// </summary>
    /// <param name="sender">The speaker or the scale.</param>
    /// <param name="e">The event data.</param>
    private void OnVolumeWheel(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.Volume.Volume = Math.Clamp(viewModel.Volume.Volume + (e.Delta.Y * 5), 0, 100);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Plays or pauses with Space and seeks with the arrows, as on the desktop; never while a text is typed or a dialog
    /// of the page is open.
    /// </summary>
    /// <param name="sender">The top level.</param>
    /// <param name="e">The event data.</param>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { IsInSession: true } viewModel || e.Source is TextBox || HasDialog(viewModel))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space when viewModel.IsFilmOnScreen:
                viewModel.TogglePlayCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Space:
                e.Handled = true;
                break;
            case Key.Left when viewModel.IsFilmOnScreen:
                viewModel.SeekBackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right when viewModel.IsFilmOnScreen:
                viewModel.SeekForwardCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Keeps the release of Space from pressing a focused button: Space belongs to play and pause.
    /// </summary>
    /// <param name="sender">The top level.</param>
    /// <param name="e">The event data.</param>
    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && ViewModel is { IsInSession: true } viewModel && e.Source is not TextBox && !HasDialog(viewModel))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Tells whether a dialog of the page is open, where the keys belong to the dialog.
    /// </summary>
    /// <param name="viewModel">The page.</param>
    /// <returns><see langword="true"/> while a dialog is open.</returns>
    private static bool HasDialog(MainViewModel viewModel)
        => viewModel.IsSettingsOpen || viewModel.IsInviteOpen || viewModel.HasQuestion || viewModel.IsLeaveConfirmOpen || viewModel.HasError;

    /// <summary>
    /// Fits the screens to the size of the page: marks the page for the styles (compact, short, stacked) and lays the
    /// participants beside the film or, on a page held upright, under it. Under the film the column gives way to the
    /// film in the full screen.
    /// </summary>
    /// <param name="size">The size of the page.</param>
    private void ApplyLayout(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var stacked = size.Height > size.Width && size.Width < STACKED_MAX_WIDTH;
        var low = size.Height < SHORT_HEIGHT;
        var compact = size.Width < COMPACT_WIDTH;
        Classes.Set("compact", compact);
        ArrangeStartCards(compact);
        Classes.Set("short", low);
        Classes.Set("stacked", stacked);

        var videoArea = this.FindControl<Panel>("VideoArea")!;
        videoArea.Height = stacked && !_fullScreen
            ? Math.Round(Math.Min(size.Width * FILM_HEIGHT_PER_WIDTH, size.Height * FILM_MAX_SHARE))
            : double.NaN;
        if (this.FindControl<ScrollViewer>("TracksScroll") is { } tracks)
        {
            tracks.MaxHeight = Math.Clamp(size.Height - TRACKS_ROOM, 0, TRACKS_MAX_HEIGHT);
        }

        if (_layout == (stacked, low, _fullScreen))
        {
            return;
        }

        _layout = (stacked, low, _fullScreen);
        ArrangeSession(stacked, low);
    }

    /// <summary>
    /// Puts the cards of the start screen side by side, in two equal columns, or one under the other on a narrow page.
    /// </summary>
    /// <param name="compact">Whether the page is narrow.</param>
    private void ArrangeStartCards(bool compact)
    {
        if (_startCompact == compact)
        {
            return;
        }

        _startCompact = compact;
        var grid = this.FindControl<Grid>("StartCards")!;
        var join = this.FindControl<Border>("JoinCard")!;

        grid.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "*,20,*");
        grid.RowDefinitions = new RowDefinitions(compact ? "Auto,16,Auto" : "Auto");
        Grid.SetColumn(join, compact ? 0 : 2);
        Grid.SetRow(join, compact ? 2 : 0);
    }

    /// <summary>
    /// Puts the participants beside the film or under it.
    /// </summary>
    /// <param name="stacked">Whether the participants lie under the film.</param>
    /// <param name="low">Whether the page is low, where the column beside the film scrolls as one.</param>
    private void ArrangeSession(bool stacked, bool low)
    {
        var grid = this.FindControl<Grid>("SessionGrid")!;
        var side = this.FindControl<Border>("SidePanel")!;

        if (stacked)
        {
            grid.ColumnDefinitions = new ColumnDefinitions("*");
            grid.RowDefinitions = new RowDefinitions(_fullScreen ? "*" : "Auto,*");
            Grid.SetColumn(side, 0);
            Grid.SetRow(side, 1);
            side.BorderThickness = new Thickness(0, 1, 0, 0);
        }
        else
        {
            grid.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(SIDE_WIDTH, GridUnitType.Pixel)];
            grid.RowDefinitions = new RowDefinitions("*");
            Grid.SetColumn(side, 1);
            Grid.SetRow(side, 0);
            side.BorderThickness = new Thickness(1, 0, 0, 0);
        }

        // Under the film, or on a low page, the column scrolls as one; otherwise only its tiles scroll.
        side.IsVisible = !(stacked && _fullScreen);
        this.FindControl<ScrollViewer>("SideScroll")!.VerticalScrollBarVisibility = stacked || low
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
    }

    /// <summary>
    /// Lets the reactions rise only as high as the film is, so over a small film they fade at its top edge.
    /// </summary>
    /// <param name="filmHeight">The height of the film area.</param>
    private void FitReactions(double filmHeight)
        => this.FindControl<Border>("ReactionsLane")!.Height = Math.Clamp(filmHeight - (2 * REACTIONS_MARGIN), 0, REACTIONS_HEIGHT);

    /// <summary>
    /// Tells the hiding logic what the page is doing and lets its time pass.
    /// </summary>
    private void UpdateChrome()
    {
        var inSession = ViewModel is { IsInSession: true };
        var overlaid = inSession && _fullScreen;

        SetBarsOverlaid(overlaid);
        _chrome.SetCursorHiding(inSession && ViewModel is { IsFilmOnScreen: true });
        _chrome.SetBarsHiding(overlaid);
        _chrome.SetHeld(inSession && IsChromeHeld());
        _chrome.Tick();
    }

    /// <summary>
    /// Moves the bars between the layout and the layers over the film.
    /// </summary>
    /// <param name="overlaid">Whether the bars lie over the film.</param>
    private void SetBarsOverlaid(bool overlaid)
    {
        if (_barsOverlaid == overlaid)
        {
            return;
        }

        _barsOverlaid = overlaid;
        var topBar = this.FindControl<SlideReveal>("TopBar")!;
        var playerBar = this.FindControl<SlideReveal>("PlayerBar")!;
        var topSlot = this.FindControl<Decorator>("TopBarSlot")!;
        var playerSlot = this.FindControl<Decorator>("PlayerBarSlot")!;
        var topLayer = this.FindControl<Decorator>("TopBarLayer")!;
        var playerLayer = this.FindControl<Decorator>("PlayerBarLayer")!;
        if (overlaid)
        {
            topSlot.Child = null;
            playerSlot.Child = null;
            topLayer.Child = topBar;
            playerLayer.Child = playerBar;
        }
        else
        {
            topLayer.Child = null;
            playerLayer.Child = null;
            topSlot.Child = topBar;
            playerSlot.Child = playerBar;

            // In the layout the bars are simply there.
            topBar.Snap(true);
            playerBar.Snap(true);
        }

        ApplyChrome();
    }

    /// <summary>
    /// Checks whether something needs the bars right now: an open menu of the bars, a drag on the timeline or the pen.
    /// </summary>
    /// <returns><see langword="true"/> while the bars must stay.</returns>
    private bool IsChromeHeld()
        => this.FindControl<Popup>("VolumePopup")?.IsOpen == true ||
            this.FindControl<Button>("TracksButton")?.Flyout?.IsOpen == true ||
            this.FindControl<Button>("ReactionsButton")?.Flyout?.IsOpen == true ||
            this.FindControl<Button>("MoreActions")?.Flyout?.IsOpen == true ||
            ViewModel is { IsScrubbing: true } or { Drawing.IsPenActive: true };

    /// <summary>
    /// Shows or hides the bars and the cursor as the hiding logic decided.
    /// </summary>
    private void ApplyChrome()
    {
        if (_barsOverlaid)
        {
            this.FindControl<SlideReveal>("TopBar")!.IsExpanded = _chrome.BarsVisible;
            this.FindControl<SlideReveal>("PlayerBar")!.IsExpanded = _chrome.BarsVisible;
        }

        // The hidden bars lie over the film: they must not take the clicks meant for it.
        this.FindControl<Decorator>("TopBarLayer")!.IsHitTestVisible = _chrome.BarsVisible;
        this.FindControl<Decorator>("PlayerBarLayer")!.IsHitTestVisible = _chrome.BarsVisible;
        this.FindControl<Panel>("VideoArea")!.Cursor = _chrome.CursorVisible ? Cursor.Default : _hiddenCursor;
    }

    /// <summary>
    /// Tells the hiding logic where the pointer is.
    /// </summary>
    /// <param name="zone">The part of the page.</param>
    private void ReportZone(PointerZone zone)
    {
        if (_chrome.IsActive)
        {
            _chrome.PointerMoved(zone);
        }
    }

    /// <summary>
    /// Names the part of the page a point is in.
    /// </summary>
    /// <param name="position">The point in the coordinates of the page.</param>
    /// <returns>The zone.</returns>
    private PointerZone ZoneAt(Point position)
    {
        var video = BoundsInPage(this.FindControl<Control>("VideoArea"));

        // The top bar spans the page; the player bar lies under the film only, and the chat is beside it.
        if (position.Y < REVEAL_EDGE ||
            (position.Y > Bounds.Height - REVEAL_EDGE && position.X < video.Right) ||
            BoundsInPage(this.FindControl<Control>("TopBar")).Contains(position) ||
            BoundsInPage(this.FindControl<Control>("PlayerBar")).Contains(position))
        {
            return PointerZone.Bars;
        }

        return video.Contains(position) ? PointerZone.Video : PointerZone.Elsewhere;
    }

    /// <summary>
    /// Gives the rectangle of a control in the coordinates of the page; empty while it is not laid out.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <returns>The rectangle.</returns>
    private Rect BoundsInPage(Control? control)
        => control?.TranslatePoint(default, this) is { } corner ? new Rect(corner, control.Bounds.Size) : default;
}
