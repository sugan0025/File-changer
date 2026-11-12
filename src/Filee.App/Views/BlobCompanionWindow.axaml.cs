using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.Services.Triggers;
using Filee.Core.Conversion;
using Filee.Core.Platform;
using Filee.Core.Settings;

namespace Filee.App.Views;

/// <summary>Native host and interaction coordinator for the desktop Blob.</summary>
public partial class BlobCompanionWindow : Window
{
    private readonly RadialController _radial;
    private readonly TriggerService _triggers;
    private readonly DispatcherTimer _behaviorTimer;
    private readonly DispatcherTimer _wheelTimer;
    private readonly DispatcherTimer _cursorTimer;
    private readonly DispatcherTimer _sleepGlyphTimer;
    private readonly DispatcherTimer _hugTimer;
    private readonly DispatcherTimer _napTimer;
    private readonly DispatcherTimer _chewTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource _thoughtCancellation = new();
    private CancellationTokenSource? _wakeCancellation;
    private CancellationTokenSource? _dropAnimationCancellation;
    private CancellationTokenSource? _desktopHopCancellation;
    private CancellationTokenSource? _cinemaCancellation;
    private readonly Random _random = new();
    private readonly BlobDragYankDetector _yankDetector = new();
    private readonly StretchReminderSchedule _stretchReminder = new();
    private ScaleTransform _fileTokenScale = new();
    private Bitmap? _fileTypeIcon;
    private PixelPoint _pressScreen;
    private PixelPoint _windowAtPress;
    private PixelPoint _lastDragScreen;
    private Point _pressLocal;
    private Point _lastPointer;
    private DateTime _idleSince = DateTime.UtcNow;
    private DateTime _lastCursorMovement = DateTime.UtcNow;
    private DateTime _nextBehavior;
    private PixelPoint? _lastCursorPosition;
    private bool _pressed;
    private bool _moving;
    private bool _dragOver;
    private PixelPoint? _desktopHopOrigin;
    private bool _dragOffersFiles;
    private bool _dropPending;
    private bool _paused;
    private bool _gestureActive;
    private bool _napping;
    private bool _licked;
    private bool _largeBatch;
    private bool _autonomousActive;
    private bool _pendingSuccess;
    private bool _pendingError;
    private bool _thoughtShowing;
    private bool _appReactionActive;
    private bool _hugCompleted;
    private bool _controlHeldAtPress;
    private bool _conversionActive;
    private bool _cinemaDimActive;
    private bool _reemerging;
    private bool _stretchReminderEnabled;
    private TimeSpan? _lastSystemIdle;
    private DateTime? _napUntil;
    private DateTime _quipsQuietUntil;
    private int _swallowedFiles;
    private DateTime _lastSwallowAt;
    private double _conversionProgress;
    private readonly Stopwatch _conversionClock = new();
    private PlatformScreenBounds? _focusedInputBounds;
    private DateTime _fileDragSince;
    private DateTime _pressStarted;
    private DateTime _lastDragAt;
    private DateTime _lastRubbingAt;
    private double _lastRubbingDx;
    private int _rubbingReversals;
    private int _rapidClicks;
    private DateTime _lastClickAt;
    private BlobCompanionActivity _activity = BlobCompanionActivity.Adaptive;
    private bool _playHatchOnNextShow;
    private int _lastBehavior = -1;
    private int _dragSequence;

    public BlobCompanionWindow(RadialController radial, TriggerService triggers)
    {
        _radial = radial;
        _triggers = triggers;
        InitializeComponent();
        _fileTokenScale = (ScaleTransform)FileToken.RenderTransform!;
        Blob.State.SuppressNonBoredHops = true;
        Blob.State.CompanionMode = true;

        _behaviorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _behaviorTimer.Tick += OnBehaviorTick;
        _wheelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _wheelTimer.Tick += OnWheelTimerTick;
        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _cursorTimer.Tick += OnCursorTimerTick;
        _sleepGlyphTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _sleepGlyphTimer.Tick += OnSleepGlyphTick;
        _hugTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _hugTimer.Tick += OnHugTimerTick;
        _napTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _napTimer.Tick += OnNapTimerTick;
        _chewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _chewTimer.Tick += OnChewTimerTick;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerEntered += (_, _) => { if (_napping) WakeAndResetIdle(); };
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            _desktopHopCancellation?.Cancel();
            _cinemaCancellation?.Cancel();
            _thoughtCancellation.Cancel();
            _thoughtCancellation.Dispose();
            _behaviorTimer.Stop();
            _wheelTimer.Stop();
            _cursorTimer.Stop();
            _sleepGlyphTimer.Stop();
            _hugTimer.Stop();
            _napTimer.Stop();
            _chewTimer.Stop();
            _fileTypeIcon?.Dispose();
            _fileTypeIcon = null;
        };

        DragDrop.AddDragEnterHandler(this, OnDragEnter);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDragLeaveHandler(this, OnDragLeave);
        DragDrop.AddDropHandler(this, OnDrop);
        _nextBehavior = DateTime.UtcNow + NextBoredomDelay();
    }

    public BlobCompanionWindow() : this(AppHost.Get<RadialController>(), AppHost.Get<TriggerService>()) { }

    public event EventHandler<PixelPoint>? PositionMoved;
    public event Action<bool>? VisibilityChanged;

    public BlobCompanionActivity Activity
    {
        get => _activity;
        set
        {
            _activity = value;
            ResetIdleTimer();
            if (value == BlobCompanionActivity.Off) _behaviorTimer.Stop();
            else if (!_paused && IsVisible) _behaviorTimer.Start();
        }
    }
    public bool CompanionAudioEnabled { get => Blob.AudioEnabled; set => Blob.AudioEnabled = value; }
    public Func<bool>? IsCompanionEnabled { get; set; }
    public bool PlayHatchOnNextShow { get => _playHatchOnNextShow; set => _playHatchOnNextShow = value; }

    public bool IsPaused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            Blob.SetPaused(value);
            if (value)
            {
                _hugTimer.Stop();
                CancelWakeRitual();
                CancelThoughtForPriority();
                CancelDesktopHop();
                Blob.NotifyDragOver(false);
                Blob.SetCurious(false);
                Blob.SetMouthOpen(0);
                ThoughtPopup.IsOpen = false;
                ThoughtCloud.Opacity = 0;
                EndFileDrag();
                _behaviorTimer.Stop();
                _cursorTimer.Stop();
                _napTimer.Stop();
                _chewTimer.Stop();
                if (IsVisible && Motion.Enabled) _sleepGlyphTimer.Start();
            }
            else
            {
                _sleepGlyphTimer.Stop();
                if (_napUntil is { } napUntil && napUntil > DateTime.UtcNow)
                {
                    _napping = true;
                    Blob.SetNapping(true);
                    _napTimer.Start();
                    if (Motion.Enabled) _sleepGlyphTimer.Start();
                }
                else
                {
                    _napUntil = null;
                    WakeAndResetIdle();
                }
                if (_conversionActive && !_cinemaDimActive && IsVisible) _chewTimer.Start();
            }
        }
    }

    public bool CanObserveAppContext => BlobAppAwareness.CanObserve(IsVisible, _paused, _gestureActive, _dragOver,
        _dropPending, _moving, _pressed, _radial.IsVisible) && !_conversionActive && !_cinemaDimActive
        && !_napping && _wakeCancellation is null;

    public bool IsQuipsQuiet => DateTime.UtcNow < _quipsQuietUntil;
    public bool StretchReminderEnabled { get => _stretchReminderEnabled; set => _stretchReminderEnabled = value; }

    public void NapFor(TimeSpan duration)
    {
        if (!IsVisible || _paused || _dropPending || _conversionActive || _radial.IsVisible) return;
        _napUntil = DateTime.UtcNow + TimeSpan.FromTicks(Math.Clamp(duration.Ticks,
            TimeSpan.FromMinutes(1).Ticks, TimeSpan.FromHours(12).Ticks));
        _napping = true;
        Blob.SetNapping(true);
        _behaviorTimer.Stop();
        _cursorTimer.Stop();
        if (Motion.Enabled) _sleepGlyphTimer.Start();
        _napTimer.Start();
    }

    public void QuietAppQuipsFor(TimeSpan duration) =>
        _quipsQuietUntil = DateTime.UtcNow + TimeSpan.FromTicks(Math.Clamp(duration.Ticks,
            TimeSpan.FromMinutes(1).Ticks, TimeSpan.FromHours(12).Ticks));

    public bool SetCinemaDimState(bool active)
    {
        if (_cinemaDimActive == active) return false;
        _cinemaDimActive = active;
        _cinemaCancellation?.Cancel();
        _cinemaCancellation?.Dispose();
        _cinemaCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        Blob.MaxFramesPerSecond = active ? 15 : 30;
        if (active) _chewTimer.Stop();
        else if (_conversionActive && IsVisible && !_paused) _chewTimer.Start();
        if (_reemerging) return true;
        if (!Motion.Enabled || !IsVisible)
        {
            Blob.Opacity = active ? .48 : 1;
            return true;
        }
        _ = AnimateCinemaDimAsync(active, _cinemaCancellation.Token);
        return true;
    }

    public void SetSystemIdleTime(TimeSpan? idle)
    {
        if (idle is not { } elapsed || elapsed < TimeSpan.Zero)
            return;
        _lastSystemIdle = elapsed;
        _idleSince = DateTime.UtcNow - elapsed;
        UpdateStretchReminder();
    }

    private void UpdateStretchReminder()
    {
        if (!Motion.Enabled) return;
        var canShow = IsVisible && !_paused && !_napping && !_moving && !_pressed && !_dragOver
            && _activity is not (BlobCompanionActivity.Calm or BlobCompanionActivity.Off)
            && !_dropPending && !_conversionActive && !_cinemaDimActive && !_thoughtShowing && !_radial.IsVisible;
        if (!_stretchReminder.Update(_lastSystemIdle, DateTime.UtcNow, _stretchReminderEnabled, canShow))
            return;

        Blob.State.TriggerStretch();
        ThoughtText.Text = "Stretch break? Let's wiggle.";
        ThoughtCloud.Opacity = 1;
        ThoughtPopup.IsOpen = true;
        _thoughtShowing = true;
        DispatcherTimer.RunOnce(() =>
        {
            if (_appReactionActive || _dropPending || _radial.IsVisible) return;
            ThoughtPopup.IsOpen = false;
            ThoughtCloud.Opacity = 0;
            ThoughtText.Text = "No files to feed me ??";
            _thoughtShowing = false;
        }, TimeSpan.FromSeconds(4));
    }

    private void OnHugTimerTick(object? sender, EventArgs e)
    {
        _hugTimer.Stop();
        if (!BlobCompanionBehavior.HugCompleted(_pressed, _moving, _controlHeldAtPress, _paused, _gestureActive)
            || _dragOver || _dropPending || _conversionActive || _cinemaDimActive || _radial.IsVisible)
            return;

        _hugCompleted = true;
        Blob.State.SetHappy(.7, 1.1);
        Blob.State.TriggerBlink(2);
        Blob.State.TriggerStretch();
    }

    private void OnNapTimerTick(object? sender, EventArgs e)
    {
        if (!BlobCompanionBehavior.NapExpired(_napUntil, DateTime.UtcNow)) return;
        _napTimer.Stop();
        _napUntil = null;
        WakeAndResetIdle();
    }

    private void OnChewTimerTick(object? sender, EventArgs e)
    {
        if (!IsVisible || !_conversionActive || _paused || _cinemaDimActive) return;
        var cycle = Math.Sin(_conversionClock.Elapsed.TotalSeconds * Math.PI * 3.2);
        Blob.SetMouthOpen(.08 + .07 * ((cycle + 1) / 2));
    }

    private async Task AnimateCinemaDimAsync(bool active, CancellationToken cancellationToken)
    {
        var start = Blob.Opacity;
        var end = active ? .48 : 1;
        try
        {
            const int steps = 8;
            for (var step = 1; step <= steps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Blob.Opacity = start + (end - start) * step / steps;
                await Task.Delay(38, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                Blob.Opacity = end;
        }
    }

    private void CancelThoughtForPriority()
    {
        if (!_thoughtShowing) return;
        _thoughtCancellation.Cancel();
        _thoughtCancellation.Dispose();
        _thoughtCancellation = new CancellationTokenSource();
        _thoughtShowing = false;
        _appReactionActive = false;
        ThoughtPopup.IsOpen = false;
        ThoughtCloud.Opacity = 0;
        ThoughtText.Text = "No files to feed me ??";
        Blob.SetCurious(false);
        if (!_paused && !_napping && IsVisible && _activity != BlobCompanionActivity.Off && !_cinemaDimActive)
            _cursorTimer.Start();
    }

    private async Task PlayWakeRitualAsync()
    {
        if (!Motion.Enabled) return;
        CancelWakeRitual();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _wakeCancellation = cancellation;
        try
        {
            Blob.State.TriggerBlink();
            Blob.State.TriggerStretch();
            await Task.Delay(260, cancellation.Token);
            if (!IsVisible || _paused) return;
            Blob.State.TriggerTiltWobble(3.5);
            Blob.State.TriggerBlink(2);
            await Task.Delay(500, cancellation.Token);
            if (!IsVisible || _paused) return;
            Blob.State.LookAt(null);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_wakeCancellation, cancellation))
                _wakeCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelWakeRitual()
    {
        _wakeCancellation?.Cancel();
        _wakeCancellation?.Dispose();
        _wakeCancellation = null;
    }

    private void UpdatePerchLean()
    {
        var scale = Math.Max(.1, RenderScaling);
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen is null)
        {
            Blob.State.SetRestLean(0);
            return;
        }
        Blob.State.SetRestLean(BlobCompanionBehavior.PerchLean(Position.X,
            (int)Math.Ceiling(Bounds.Width * scale), screen.WorkingArea.X, screen.WorkingArea.Width, scale));
    }

    private void AddSwallowedFiles(int count)
    {
        _swallowedFiles = Math.Min(5, _swallowedFiles + Math.Max(1, count));
        _lastSwallowAt = DateTime.UtcNow;
        Blob.State.SetCompanionFullness(BlobCompanionBehavior.Fullness(_swallowedFiles, TimeSpan.Zero));
    }

    private void UpdateFullness()
    {
        if (_lastSwallowAt == default) return;
        var elapsed = DateTime.UtcNow - _lastSwallowAt;
        var fullness = BlobCompanionBehavior.Fullness(_swallowedFiles, elapsed);
        Blob.State.SetCompanionFullness(fullness);
        if (fullness <= 0)
        {
            _swallowedFiles = 0;
            _lastSwallowAt = default;
        }
    }

    public void SetFocusedInputBounds(PlatformScreenBounds? bounds)
    {
        if (_focusedInputBounds is not null && bounds is null)
        {
            var (x, y) = _triggers.LastCursor;
            _lastCursorPosition = new PixelPoint(x, y);
            _lastCursorMovement = DateTime.UtcNow;
        }
        _focusedInputBounds = bounds;
    }

    public bool ShowAppReaction(string text, string? appKey = null)
    {
        var currentMoment = _dropPending || _radial.IsVisible ? BlobCompanionMoment.FileDrop
            : _conversionActive ? BlobCompanionMoment.Conversion
            : _gestureActive || _pressed || _moving ? BlobCompanionMoment.UserReaction
            : _thoughtShowing && !_appReactionActive ? BlobCompanionMoment.UserReaction
            : _appReactionActive ? BlobCompanionMoment.AppReaction
            : BlobCompanionMoment.Idle;
        if (IsQuipsQuiet || !CanObserveAppContext || string.IsNullOrWhiteSpace(text)
            || !BlobCompanionBehavior.CanInterrupt(currentMoment, BlobCompanionMoment.AppReaction))
            return false;
        if (_appReactionActive)
            CancelThoughtForPriority();
        _appReactionActive = true;
        _ = ShowAppReactionAsync(text, BlobCompanionBehavior.MotionForApp(appKey));
        return true;
    }

    public bool GestureActive
    {
        get => _gestureActive;
        set
        {
            _gestureActive = value;
            if (value)
            {
                CancelWakeRitual();
                CancelThoughtForPriority();
            }
            if (value && _dragOver)
                EndFileDrag();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty)
            return;
        if (change.GetNewValue<bool>())
        {
            VisibilityChanged?.Invoke(true);
            Opacity = 1;
            Blob.Opacity = 1;
            _idleSince = DateTime.UtcNow;
            if (!_paused)
            {
                _cursorTimer.Start();
                if (_activity != BlobCompanionActivity.Off) _behaviorTimer.Start();
                if (_conversionActive) _chewTimer.Start();
                if (_napUntil is not null)
                {
                    if (BlobCompanionBehavior.NapExpired(_napUntil, DateTime.UtcNow))
                        WakeAndResetIdle();
                    else _napTimer.Start();
                }
            }
            else
            {
                if (_paused && Motion.Enabled) _sleepGlyphTimer.Start();
            }
            if (_playHatchOnNextShow)
            {
                _playHatchOnNextShow = false;
                _ = PlayHatchAsync();
            }
        }
        else
        {
            Blob.State.SuppressAutonomousWinks = false;
            VisibilityChanged?.Invoke(false);
            CancelDesktopHop();
            if (_dropPending) _dropAnimationCancellation?.Cancel();
            _thoughtCancellation.Cancel();
            _thoughtCancellation.Dispose();
            _thoughtCancellation = new CancellationTokenSource();
            _thoughtShowing = false;
            _appReactionActive = false;
            _behaviorTimer.Stop();
            _cursorTimer.Stop();
            _sleepGlyphTimer.Stop();
            _hugTimer.Stop();
            _napTimer.Stop();
            _chewTimer.Stop();
            CancelWakeRitual();
            SetCinemaDimState(false);
            Blob.Opacity = 1;
            Blob.RenderTransform = null;
            ThoughtPopup.IsOpen = false;
            ThoughtCloud.Opacity = 0;
            _moving = _pressed = false;
            _dragOver = _dragOffersFiles = false;
            Blob.NotifyDragOver(false);
            Blob.SetCurious(false);
            Blob.SetMouthOpen(0);
            Blob.State.SetDragMotion(0, 0);
        }
    }

    private void OnCursorTimerTick(object? sender, EventArgs e)
    {
        if (_paused || _napping || !IsVisible || !Blob.IsVisible)
            return;
        UpdateFullness();

        var (x, y) = _triggers.LastCursor;
        var cursor = new PixelPoint(x, y);
        if (_lastCursorPosition is not { } previous || Math.Abs(cursor.X - previous.X) + Math.Abs(cursor.Y - previous.Y) > 2)
        {
            _lastCursorPosition = cursor;
            _lastCursorMovement = DateTime.UtcNow;
        }
        if (_focusedInputBounds is { } focused)
        {
            var focusedOrigin = Blob.PointToScreen(new Point(0, 0));
            Blob.State.LookAtCursor(BlobFocusGaze.ToViewBoxTarget(focused, focusedOrigin, RenderScaling, new Rect(Blob.Bounds.Size)));
            return;
        }

        if (DateTime.UtcNow - _lastCursorMovement >= TimeSpan.FromSeconds(4))
        {
            Blob.State.LookAt(null);
            return;
        }

        var origin = Blob.PointToScreen(new Point(0, 0));
        var scale = Math.Max(.1, RenderScaling);
        var local = new Point((x - origin.X) / scale, (y - origin.Y) / scale);
        Blob.State.LookAtCursor(BlobatarRenderer.ToViewBox(local, new Rect(Blob.Bounds.Size)));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _radial.IsVisible || _dropPending || _conversionActive)
            return;

        CancelThoughtForPriority();

        _pressed = true;
        _moving = false;
        _hugCompleted = false;
        _controlHeldAtPress = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        _yankDetector.Reset();
        _pressLocal = e.GetPosition(this);
        _pressScreen = Blob.PointToScreen(e.GetPosition(Blob));
        _pressStarted = DateTime.UtcNow;
        _lastDragScreen = _pressScreen;
        _lastDragAt = _pressStarted;
        _windowAtPress = Position;
        e.Pointer.Capture(this);
        e.Handled = true;
        Blob.PressForReaction();
        if (!_controlHeldAtPress) _hugTimer.Start();
        ResetIdleTimer();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var local = e.GetPosition(this);
        if (_pressed && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var screen = Blob.PointToScreen(e.GetPosition(Blob));
            var dx = screen.X - _pressScreen.X;
            var dy = screen.Y - _pressScreen.Y;
            if (!_moving && DateTime.UtcNow - _pressStarted >= TimeSpan.FromMilliseconds(150)
                && Math.Sqrt(dx * (double)dx + dy * (double)dy) >= 5)
            {
                _moving = true;
                _hugTimer.Stop();
                _hugCompleted = false;
                CancelDesktopHop();
                ThoughtPopup.IsOpen = false;
                Blob.SetCurious(false);
                Blob.SetMouthOpen(0);
                Blob.State.SetDragOver(false);
                Blob.ReleaseForReaction();
            }

            if (_moving)
            {
                _yankDetector.Record(screen.X - _lastDragScreen.X, screen.Y - _lastDragScreen.Y, DateTime.UtcNow);
                Position = new PixelPoint(_windowAtPress.X + dx, _windowAtPress.Y + dy);
                PositionMoved?.Invoke(this, Position);
                var now = DateTime.UtcNow;
                var elapsed = Math.Max(.01, (now - _lastDragAt).TotalSeconds);
                var velocityX = (screen.X - _lastDragScreen.X) / elapsed;
                var velocityY = (screen.Y - _lastDragScreen.Y) / elapsed;
                var velocity = Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
                Blob.State.SetDragMotion(velocityX / 80, velocity / 10000);
                Blob.State.LookAt(new Point(Math.Sign(dx) * 18, Math.Sign(dy) * 10));
                _lastDragScreen = screen;
                _lastDragAt = now;
                e.Handled = true;
            }
            return;
        }

        if (Math.Abs(local.X - _lastPointer.X) > 3 || Math.Abs(local.Y - _lastPointer.Y) > 3)
        {
            ResetIdleTimer();
            if (_napping) WakeAndResetIdle();
            var now = DateTime.UtcNow;
            var dx = local.X - _lastPointer.X;
            if (now - _lastRubbingAt < TimeSpan.FromSeconds(2) && dx * _lastRubbingDx < 0)
                _rubbingReversals++;
            else
                _rubbingReversals = 0;
            _lastRubbingAt = now;
            _lastRubbingDx = dx;
            if (_rubbingReversals >= 4 && !_paused)
            {
                _rubbingReversals = 0;
                Blob.State.SetHappy(1, 1.1);
                Blob.State.TriggerTiltWobble(6);
            }
        }
        _lastPointer = local;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed)
            return;

        _hugTimer.Stop();
        _pressed = false;
        e.Pointer.Capture(null);
        Blob.ReleaseForReaction();
        if (_moving)
        {
            _moving = false;
            Blob.State.SetDragMotion(0, 0);
            UpdatePerchLean();
            if (_yankDetector.IsYank(DateTime.UtcNow))
            {
                Blob.State.TriggerCompanionEyeRoll(10, 1.4);
                Blob.State.TriggerTiltWobble(7);
                Blob.State.TriggerBlink(2);
            }
            else Blob.State.TriggerTiltWobble(4.5);
            _yankDetector.Reset();
            e.Handled = true;
            return;
        }

        e.Handled = true;
        if (_hugCompleted)
        {
            _hugCompleted = false;
            ResetIdleTimer();
            return;
        }
        var releasedAt = DateTime.UtcNow;
        _rapidClicks = releasedAt - _lastClickAt <= TimeSpan.FromSeconds(1.2) ? _rapidClicks + 1 : 1;
        _lastClickAt = releasedAt;
        if (_rapidClicks == 3)
        {
            Blob.State.TriggerTiltWobble(9);
            Blob.State.TriggerWink();
        }
        else if (_rapidClicks >= 6)
        {
            Blob.State.TriggerTiltWobble(13);
            Blob.State.TriggerBlink(3);
            _rapidClicks = 0;
        }
        _ = ShowThoughtCloudAsync();
    }

    private async Task ShowThoughtCloudAsync()
    {
        if (_thoughtShowing || _dropPending || _conversionActive || _radial.IsVisible) return;
        _thoughtShowing = true;
        var source = _thoughtCancellation;
        var token = source.Token;
        try
        {
            Blob.State.LookAtCursor(new Point(52.55, 44.8));
            ThoughtText.Text = _paused ? "...paused" : "No files to feed me ??";
            Blob.SetCurious(true);
            ThoughtCloud.Opacity = 0;
            ThoughtPopup.IsOpen = true;
            await Task.Delay(20, token);
            ThoughtCloud.Opacity = 1;
            await Task.Delay(100, token);
            await Task.Delay(2200, token);
            ThoughtCloud.Opacity = 0;
            await Task.Delay(260, token);
            ThoughtPopup.IsOpen = false;
            Blob.SetCurious(false);
            ThoughtText.Text = "No files to feed me ??";
            Blob.State.LookAt(null);
            ResetIdleTimer();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(source, _thoughtCancellation))
                _thoughtShowing = false;
        }
    }

    private async Task ShowAppReactionAsync(string text, BlobAppBodyMotion motion)
    {
        _thoughtShowing = true;
        var source = _thoughtCancellation;
        var token = source.Token;
        try
        {
            _cursorTimer.Stop();
            Blob.State.LookAtCursor(new Point(52.55, 44.8));
            Blob.SetCurious(true);
            ThoughtText.Text = text;
            ThoughtCloud.Opacity = 0;
            ThoughtPopup.IsOpen = true;
            await Task.Delay(20, token);
            ThoughtCloud.Opacity = 1;
            await PlayAppBodyLanguageAsync(motion, token);
            await Task.Delay(2400, token);
            ThoughtCloud.Opacity = 0;
            await Task.Delay(260, token);
            ThoughtPopup.IsOpen = false;
            Blob.SetCurious(false);
            ThoughtText.Text = "No files to feed me ??";
            Blob.State.LookAt(null);
            ResetIdleTimer();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(source, _thoughtCancellation))
            {
                ThoughtPopup.IsOpen = false;
                ThoughtCloud.Opacity = 0;
                ThoughtText.Text = "No files to feed me ??";
                Blob.SetCurious(false);
                if (!_paused && IsVisible && _activity != BlobCompanionActivity.Off)
                    _cursorTimer.Start();
                _thoughtShowing = false;
                _appReactionActive = false;
            }
        }
    }

    private async Task PlayAppBodyLanguageAsync(BlobAppBodyMotion motion, CancellationToken token)
    {
        switch (motion)
        {
            case BlobAppBodyMotion.Scan:
                Blob.State.LookAt(new Point(-18, 0));
                await Task.Delay(220, token);
                Blob.State.LookAt(new Point(18, 0));
                await Task.Delay(220, token);
                Blob.State.LookAt(null);
                Blob.State.TriggerBlink();
                break;
            case BlobAppBodyMotion.Think:
                Blob.State.LookAt(new Point(-8, -14));
                await Task.Delay(260, token);
                Blob.State.LookAt(new Point(8, -10));
                await Task.Delay(260, token);
                Blob.State.TriggerBlink();
                Blob.State.LookAt(null);
                break;
            case BlobAppBodyMotion.Orbit:
                foreach (var target in new[] { new Point(-10, -12), new Point(12, -10), new Point(12, 8), new Point(-10, 8) })
                {
                    Blob.State.LookAt(target);
                    await Task.Delay(140, token);
                }
                Blob.State.LookAt(null);
                break;
            case BlobAppBodyMotion.Proud:
                Blob.State.Proud();
                Blob.State.TriggerBlink();
                break;
            case BlobAppBodyMotion.Settle:
                Blob.PressForReaction();
                await Task.Delay(160, token);
                Blob.ReleaseForReaction();
                Blob.State.SetHappy(.55, 1);
                break;
            case BlobAppBodyMotion.Sway:
                Blob.State.TriggerTiltWobble(-4);
                await Task.Delay(220, token);
                Blob.State.TriggerTiltWobble(7);
                await Task.Delay(220, token);
                Blob.State.TriggerStretch();
                break;
            case BlobAppBodyMotion.DoubleBlink:
                Blob.State.TriggerBlink(2);
                Blob.State.TriggerTiltWobble(2.5);
                break;
        }
    }

    public void SetGestureActive(bool active) => GestureActive = active;
    public void WaitForWheelClose() => _wheelTimer.Start();

    private bool CanAcceptFiles => !_paused && !_gestureActive && !_dropPending && !_radial.IsVisible;

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (_paused)
        {
            Blob.State.TriggerWink();
            ThoughtText.Text = "Paused";
            ThoughtCloud.Opacity = 1;
            ThoughtPopup.IsOpen = true;
            DispatcherTimer.RunOnce(() => { ThoughtPopup.IsOpen = false; ThoughtCloud.Opacity = 0; ThoughtText.Text = "No files to feed me ??"; }, TimeSpan.FromSeconds(1));
        }
        var offered = DraggedFiles.Read(e.DataTransfer, forDrop: false);
        _dragOffersFiles = CanAcceptFiles && BlobCompanionBehavior.CanWakeForFiles(_paused, offered.Files.Count);
        offered.Web?.Dispose();
        if (!_dragOffersFiles)
        {
            if (!_paused && !_gestureActive && !_radial.IsVisible)
            {
                ThoughtText.Text = "?";
                ThoughtCloud.Opacity = 1;
                ThoughtPopup.IsOpen = true;
                Blob.State.TriggerTiltWobble(5);
                DispatcherTimer.RunOnce(() => { ThoughtPopup.IsOpen = false; ThoughtCloud.Opacity = 0; ThoughtText.Text = "No files to feed me ??"; }, TimeSpan.FromMilliseconds(650));
            }
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        CancelThoughtForPriority();
        if (_napping)
            WakeAndResetIdle();
        else
            _ = PlayWakeRitualAsync();

        Blob.State.SuppressAutonomousWinks = true;
        _dragOver = true;
        CancelDesktopHop();
        _dragSequence++;
        _fileDragSince = DateTime.UtcNow;
        _licked = false;
        _largeBatch = offered.Files.Count > 5;
        Blob.NotifyDragOver(true);
        Blob.SetToneQuiet(BlobatarTone.Anticipation);
        Blob.State.TriggerTiltWobble(3.5);
        Blob.SetCurious(true);
        Blob.SetMouthOpen(.38);
        ReactToFileBatch(offered.Files);
        ResetIdleTimer();
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (_dragOffersFiles && CanAcceptFiles)
        {
            var p = e.GetPosition(Blob);
            var viewPoint = BlobatarRenderer.ToViewBox(p, new Rect(Blob.Bounds.Size));
            var distance = Math.Sqrt(Math.Pow(viewPoint.X - BlobatarRenderer.FaceX, 2) + Math.Pow(viewPoint.Y - 57.0, 2));
            var proximityOpen = .35 + .65 * Math.Clamp(1 - distance / 58, 0, 1);
            Blob.SetMouthOpen(Math.Max(_largeBatch ? .65 : .35, proximityOpen));
            Blob.State.LookAtCursor(viewPoint);
            if (!_licked && DateTime.UtcNow - _fileDragSince >= TimeSpan.FromSeconds(2))
            {
                _licked = true;
                Blob.State.TriggerBlink();
            }
        }
        e.DragEffects = CanAcceptFiles && _dragOffersFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        EndFileDrag();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!CanAcceptFiles)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            EndFileDrag();
            return;
        }

        var dropped = DraggedFiles.Read(e.DataTransfer, forDrop: true);
        if (dropped.Files.Count == 0)
        {
            dropped.Web?.Dispose();
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            EndFileDrag();
            return;
        }

        _dropPending = true;
        CancelWakeRitual();
        _dragOver = false;
        _dragSequence++;
        _dragOffersFiles = false;
        _licked = false;
        _largeBatch = false;
        e.DragEffects = DragDropEffects.Copy; // Keep the source files in Explorer.
        e.Handled = true;
        Blob.NotifyDragOver(false);
        Blob.SetCurious(true);
        _ = SwallowThenOpenWheelAsync(dropped, Blob.PointToScreen(e.GetPosition(Blob)), e.GetPosition(this));
    }

    private void ReactToFileBatch(System.Collections.Generic.IReadOnlyList<string> files)
    {
        if (files.Count > 5)
        {
            Blob.State.TriggerTiltWobble(5);
            Blob.SetMouthOpen(1);
            return;
        }
        var extension = System.IO.Path.GetExtension(files[0]).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp")
            Blob.State.TriggerBlink(2);
        else if (extension is ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg")
            Blob.State.TriggerTiltWobble(3);
        else if (extension is ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm")
            Blob.State.LookAt(new Point(0, -20));
        else if (extension is ".pdf" or ".doc" or ".docx" or ".txt" or ".rtf")
            Blob.State.TriggerBlink();
        else if (extension is ".zip" or ".7z" or ".rar" or ".tar" or ".gz")
        {
            Blob.PressForReaction();
            Blob.ReleaseForReaction();
        }
    }

    private void EndFileDrag()
    {
        if (!_dragOver) return;
        _dragOver = false;
        Blob.State.SuppressAutonomousWinks = false;
        _dragOffersFiles = false;
        _licked = false;
        _largeBatch = false;
        Blob.NotifyDragOver(false);
        Blob.SetCurious(false);
        _ = RelaxAfterFileDragAsync(++_dragSequence, Blob.MouthOpen);
        ResetIdleTimer();
    }

    private async Task SwallowThenOpenWheelAsync(DraggedItems dropped, PixelPoint at, Point local)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _dropAnimationCancellation = cancellation;
        try
        {
            if (!Motion.Enabled)
            {
                Blob.SetMouthOpen(0);
                _radial.ShowForFiles(at.X, at.Y, dropped.Files, web: dropped.Web);
                _dropPending = false;
                if (_radial.IsVisible)
                {
                    AddSwallowedFiles(dropped.Files.Count);
                    _wheelTimer.Start();
                    Hide();
                }
                else
                {
                    dropped.Web?.Dispose();
                    Blob.SetToneQuiet(BlobatarTone.Idle);
                    ResetIdleTimer();
                }
                return;
            }

            var (tokenStart, tokenEnd) = PlayFileToken(dropped.Files, local);
            await AnimateSwallowAsync(tokenStart, tokenEnd, cancellation.Token);
            Blob.SetMouthOpen(0);
            Blob.SetCurious(false);

            // The normal gesture still wins if it began while the Blob was swallowing.
            if (_gestureActive || _radial.IsVisible)
            {
                dropped.Web?.Dispose();
                _dropPending = false;
                Blob.SetMouthOpen(0);
                Blob.SetToneQuiet(BlobatarTone.Idle);
                Blob.SetPaused(_paused);
                return;
            }

            // Open the existing wheel first; the Blob contracts toward its hub as the wheel appears.
            _radial.ShowForFiles(at.X, at.Y, dropped.Files, web: dropped.Web);
            _dropPending = false;
            if (_radial.IsVisible)
            {
                AddSwallowedFiles(dropped.Files.Count);
                await AnimateWheelHandoffAsync(at);
                _wheelTimer.Start();
                Hide();
            }
            else
            {
                dropped.Web?.Dispose();
                Blob.SetToneQuiet(BlobatarTone.Idle);
                ResetIdleTimer();
            }
        }
        catch (OperationCanceledException)
        {
            dropped.Web?.Dispose();
            _dropPending = false;
            Blob.SetMouthOpen(0);
            Blob.SetToneQuiet(BlobatarTone.Idle);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Trace.TraceError("Blob companion could not hand files to the radial wheel: {0}", ex);
            dropped.Web?.Dispose();
            _dropPending = false;
            Blob.SetMouthOpen(0);
            Blob.SetToneQuiet(BlobatarTone.Idle);
        }
        finally
        {
            Blob.State.SuppressAutonomousWinks = false;
            if (ReferenceEquals(_dropAnimationCancellation, cancellation))
                _dropAnimationCancellation = null;
            cancellation.Dispose();
        }
    }

    private async void OnWheelTimerTick(object? sender, EventArgs e)
    {
        if (_radial.IsVisible) return;
        _wheelTimer.Stop();
        if (IsCompanionEnabled?.Invoke() != true) return;
        _reemerging = true;
        try
        {
            Show();
            await PlayReemergenceAsync();
        }
        finally { _reemerging = false; }
        if (!IsVisible) return;
        WakeAndResetIdle();
        Blob.SetToneQuiet(BlobatarTone.Idle);
        ApplyPendingConversionResult();
    }

    private async Task PlayReemergenceAsync()
    {
        if (!Motion.Enabled)
        {
            Blob.RenderTransform = null;
            Blob.Opacity = _cinemaDimActive ? .48 : 1;
            Blob.State.TriggerBlink();
            return;
        }

        try
        {
            const int frames = 10;
            for (var index = 0; index <= frames; index++)
            {
                if (!IsVisible || _paused) return;
                var progress = index / (double)frames;
                var frame = BlobCompanionBehavior.ReemergenceAt(progress);
                Blob.RenderTransform = new ScaleTransform(frame.Scale, frame.Scale);
                Blob.Opacity = frame.Opacity * (_cinemaDimActive ? .48 : 1);
                if (index == 6) Blob.State.TriggerBlink();
                await Task.Delay(30, _lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Blob.RenderTransform = null;
            Blob.Opacity = _cinemaDimActive ? .48 : 1;
        }
    }

    private void OnBehaviorTick(object? sender, EventArgs e)
    {
        if (!Motion.Enabled || _paused || _activity == BlobCompanionActivity.Off || _moving || _pressed || _dragOver
            || _dropPending || _autonomousActive || _thoughtShowing || _conversionActive || _cinemaDimActive || !IsVisible)
            return;

        var idle = DateTime.UtcNow - _idleSince;
        if (idle >= TimeSpan.FromMinutes(8))
        {
            if (!_napping)
            {
                _napping = true;
                Blob.SetNapping(true);
                _cursorTimer.Stop();
                _behaviorTimer.Stop();
                if (Motion.Enabled) _sleepGlyphTimer.Start();
            }
            return;
        }

        if (_napping)
            return;

        if (DateTime.UtcNow < _nextBehavior)
            return;

        var behavior = _random.Next(8);
        if (behavior == _lastBehavior) behavior = (behavior + 1 + _random.Next(7)) % 8;
        _lastBehavior = behavior;
        switch (behavior)
        {
            case 0: _ = PerformDesktopHopAsync(); break;
            case 1: Blob.TriggerStretch(); break;
            case 2: _ = YawnAsync(); break;
            case 3: Blob.State.TriggerTiltWobble(3.0); break;
            case 4: Blob.State.TriggerBlink(2); Blob.State.TriggerTiltWobble(2); break;
            case 5: Blob.State.LookAt(new Point(_random.Next(-35, 36), _random.Next(-20, 21))); break;
            case 6: _ = ShowCompanionGlyphAsync("○"); break;
            default: _ = HiccupAsync(); break;
        }
        _nextBehavior = DateTime.UtcNow + NextBoredomDelay(idle);
    }

    private TimeSpan NextBoredomDelay(TimeSpan idle = default)
    {
        var (min, max) = _activity switch
        {
            BlobCompanionActivity.Calm => (90, 180),
            BlobCompanionActivity.Playful => (30, 75),
            _ => (60, 120),
        };
        if (idle >= TimeSpan.FromMinutes(3)) min = Math.Max(25, min / 2);
        return TimeSpan.FromSeconds(min + _random.Next(max - min + 1));
    }

    private async Task PerformDesktopHopAsync()
    {
        if (!IsVisible || _paused || _dragOver || _dropPending || _moving || _gestureActive || _radial.IsVisible)
            return;

        _autonomousActive = true;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _desktopHopCancellation = cancellation;
        var origin = Position;
        _desktopHopOrigin = origin;
        var scale = Math.Max(1, RenderScaling);
        var screen = Screens.ScreenFromPoint(origin) ?? Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(origin.X, origin.Y, 800, 600);
        var direction = _random.Next(2) == 0 ? -1 : 1;
        var horizontal = Math.Max(1, (int)Math.Round(22 * scale));
        var vertical = Math.Max(1, (int)Math.Round(10 * scale));
        horizontal = Math.Min(horizontal, Math.Max(1, area.Width / 8));
        vertical = Math.Min(vertical, Math.Max(1, area.Height / 8));

        try
        {
            Blob.TriggerBoredHop();
            const int frames = 12;
            for (var frame = 1; frame <= frames; frame++)
            {
                await Task.Delay(33, cancellation.Token);
                if (!IsVisible || _paused || _dragOver || _dropPending || _moving || _gestureActive || _radial.IsVisible)
                    return;
                var t = frame / (double)frames;
                var arc = Math.Sin(Math.PI * t);
                var x = origin.X + (int)Math.Round(direction * horizontal * arc);
                var y = origin.Y - (int)Math.Round(vertical * arc);
                var maxX = Math.Max(area.X, area.Right - (int)Math.Ceiling(Bounds.Width * scale));
                var maxY = Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(Bounds.Height * scale));
                Position = new PixelPoint(Math.Clamp(x, area.X, maxX), Math.Clamp(y, area.Y, maxY));
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (IsVisible && !_moving)
                Position = origin;
            _desktopHopOrigin = null;
            if (ReferenceEquals(_desktopHopCancellation, cancellation))
                _desktopHopCancellation = null;
            cancellation.Dispose();
            _autonomousActive = false;
        }
    }

    private void CancelDesktopHop()
    {
        if (_desktopHopCancellation is null) return;
        _desktopHopCancellation.Cancel();
        if (IsVisible && !_moving && _desktopHopOrigin is { } origin)
            Position = origin;
        Blob.CancelBoredHop();
    }

    private void ResetIdleTimer()
    {
        _idleSince = DateTime.UtcNow;
        _nextBehavior = _idleSince + NextBoredomDelay();
    }

    private void WakeAndResetIdle()
    {
        var wasNapping = _napping;
        _sleepGlyphTimer.Stop();
        _napping = false;
        _napUntil = null;
        _napTimer.Stop();
        Blob.SetNapping(false);
        Blob.SetPaused(_paused);
        ResetIdleTimer();
        if (!_paused && IsVisible)
        {
            if (_activity != BlobCompanionActivity.Off) _behaviorTimer.Start();
            _cursorTimer.Start();
            if (wasNapping && Motion.Enabled)
                _ = PlayWakeRitualAsync();
        }
    }

    public void ShowConversionProgress(double progress)
    {
        _conversionActive = true;
        _conversionProgress = Math.Clamp(progress, 0, 1);
        if (_thoughtShowing) CancelThoughtForPriority();
        _conversionClock.Start();
        if (!IsVisible) return;
        Blob.SetToneQuiet(BlobatarTone.Busy);
        Blob.SetThinking(true, _conversionProgress);
        if (!_paused && !_cinemaDimActive) _chewTimer.Start();
    }

    public void ShowConversionResult(ConversionJob job)
    {
        _conversionActive = false;
        _chewTimer.Stop();
        _conversionClock.Stop();
        if (!_dropPending) Blob.SetMouthOpen(0);
        if (job.State is JobState.Completed or JobState.CompletedWithErrors)
            _pendingSuccess = job.Files.Any(f => f.State == FileState.Done);
        if (job.State is JobState.Failed or JobState.CompletedWithErrors)
        {
            _pendingError = true;
            _pendingSuccess = false;
        }
        if (IsVisible) ApplyPendingConversionResult();
    }

    private void ApplyPendingConversionResult()
    {
        if (!IsVisible) return;
        if (_conversionActive)
        {
            Blob.SetToneQuiet(BlobatarTone.Busy);
            Blob.SetThinking(true, _conversionProgress);
            if (!_paused && !_cinemaDimActive) _chewTimer.Start();
            return;
        }
        Blob.SetThinking(false);
        if (_pendingError)
        {
            _pendingError = false;
            Blob.SetToneQuiet(BlobatarTone.Alert);
            Blob.State.TriggerTiltWobble(6);
            Blob.State.TriggerBlink();
            _ = PlayDigestResultAsync(false);
            _ = RecoverFromErrorAsync();
        }
        else if (_pendingSuccess)
        {
            _pendingSuccess = false;
            Blob.SetToneQuiet(BlobatarTone.Celebration);
            Blob.State.SetHappy(1, 1.6);
            Blob.State.TriggerBlink(2);
            _ = PlayDigestResultAsync(true);
        }
        else
        {
            Blob.SetToneQuiet(BlobatarTone.Idle);
        }
    }

    private async Task PlayDigestResultAsync(bool success)
    {
        if (!IsVisible || _paused || _dropPending || _radial.IsVisible) return;
        var token = _lifetime.Token;
        try
        {
            Blob.SetMouthOpen(success ? .14 : .52);
            await Task.Delay(success ? 180 : 320, token);
            Blob.SetMouthOpen(0);
            if (success) Blob.State.TriggerStretch();
            else Blob.State.TriggerTiltWobble(-3.5);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RecoverFromErrorAsync()
    {
        try
        {
            await Task.Delay(1500, _lifetime.Token);
            Blob.SetToneQuiet(BlobatarTone.Idle);
        }
        catch (OperationCanceledException) { }
    }

    private async Task YawnAsync()
    {
        _autonomousActive = true;
        try
        {
            Blob.SetMouthOpen(.15);
            await Task.Delay(350, _lifetime.Token);
            if (_paused || _napping || !IsVisible || _dragOver || _dropPending || _moving || _pressed) return;
            Blob.SetMouthOpen(.8);
            await Task.Delay(500, _lifetime.Token);
            if (_paused || _napping || !IsVisible || _dragOver || _dropPending || _moving || _pressed) return;
            Blob.SetMouthOpen(0);
            await Task.Delay(250, _lifetime.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _autonomousActive = false;
            if ((_paused || !IsVisible) && !_dropPending) Blob.SetMouthOpen(0);
        }
    }

    private void OnSleepGlyphTick(object? sender, EventArgs e)
    {
        _sleepGlyphTimer.Interval = TimeSpan.FromSeconds(4 + _random.NextDouble() * 2);
        _ = ShowCompanionGlyphAsync("z");
    }

    private async Task HiccupAsync()
    {
        _autonomousActive = true;
        try
        {
            for (var i = 0; i < 3; i++)
            {
                if (_paused || _napping || !IsVisible) return;
                Blob.State.TriggerTiltWobble(i % 2 == 0 ? 5 : -5);
                Blob.SetMouthOpen(.3);
                await ShowCompanionGlyphAsync("hic!");
                if (_paused || _napping || !IsVisible || _dragOver || _dropPending || _moving || _pressed) return;
                Blob.SetMouthOpen(0);
                await Task.Delay(180, _lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { _autonomousActive = false; }
    }

    private async Task ShowCompanionGlyphAsync(string text)
    {
        if (!IsVisible) return;
        CompanionGlyph.Text = text;
        CompanionGlyph.Opacity = 1;
        CompanionGlyph.IsVisible = true;
        var x = text == "z" ? 67 : 10;
        Canvas.SetLeft(CompanionGlyph, x);
        Canvas.SetTop(CompanionGlyph, 24);
        try
        {
            for (var i = 1; i <= 12; i++)
            {
                await Task.Delay(55, _lifetime.Token);
                if (!IsVisible || (text != "z" && (_paused || _dragOver || _dropPending || _moving || _pressed))) return;
                Canvas.SetTop(CompanionGlyph, 24 - i * 2.2);
                CompanionGlyph.Opacity = 1 - i / 12d;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            CompanionGlyph.IsVisible = false;
            CompanionGlyph.Opacity = 0;
        }
    }

    private (Point Start, Point End) PlayFileToken(System.Collections.Generic.IReadOnlyList<string> files, Point local)
    {
        var extension = System.IO.Path.GetExtension(files[0]).TrimStart('.').ToUpperInvariant();
        FileTokenLabel.Text = files.Count > 1 ? $"{files.Count} FILES" : string.IsNullOrWhiteSpace(extension) ? "FILE" : extension[..Math.Min(4, extension.Length)];
        _fileTypeIcon?.Dispose();
        _fileTypeIcon = WindowsFileTypeIconProvider.TryGet(files[0]);
        FileTokenIcon.Source = _fileTypeIcon;
        FileTokenIcon.IsVisible = _fileTypeIcon is not null;
        var halfWidth = FileToken.Width / 2;
        var halfHeight = FileToken.Height / 2;
        var start = new Point(
            Math.Clamp(local.X - halfWidth, 0, Math.Max(0, Bounds.Width - FileToken.Width)),
            Math.Clamp(local.Y - halfHeight, 0, Math.Max(0, Bounds.Height - FileToken.Height)));
        var mouthInBlob = new Point(Blob.Bounds.Width * BlobatarRenderer.FaceX / 100, Blob.Bounds.Height * 57.0 / 100);
        var mouthInWindow = ScreenToClient(Blob.PointToScreen(mouthInBlob));
        var end = new Point(mouthInWindow.X - halfWidth, mouthInWindow.Y - halfHeight);
        Canvas.SetLeft(FileToken, start.X);
        Canvas.SetTop(FileToken, start.Y);
        FileToken.IsVisible = true;
        FileToken.Opacity = 1;
        _fileTokenScale.ScaleX = _fileTokenScale.ScaleY = 1;
        return (start, end);
    }

    private async Task AnimateSwallowAsync(Point start, Point end, CancellationToken cancellationToken)
    {
        const int frameCount = 15;
        const double durationMs = 500;
        var timer = Stopwatch.StartNew();
        var gulped = false;
        var eyeReactionStarted = false;
        try
        {
            for (var frameIndex = 0; frameIndex <= frameCount; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var progress = frameIndex == frameCount ? 1 : Math.Clamp(timer.Elapsed.TotalMilliseconds / durationMs, 0, 1);
                var state = BlobSwallowTimeline.At(progress);
                var arc = Math.Sin(state.TokenProgress * Math.PI) * 9;
                Canvas.SetLeft(FileToken, start.X + (end.X - start.X) * state.TokenProgress);
                Canvas.SetTop(FileToken, start.Y + (end.Y - start.Y) * state.TokenProgress - arc);
                _fileTokenScale.ScaleX = _fileTokenScale.ScaleY = state.TokenScale;
                FileToken.Opacity = state.TokenOpacity;
                FileToken.IsVisible = state.TokenVisible;
                Blob.SetMouthOpen(state.MouthOpen);
                if (!eyeReactionStarted && state.EyeBlink > .04)
                {
                    eyeReactionStarted = true;
                    Blob.State.TriggerBlink();
                }
                if (state.Gulp && !gulped)
                {
                    gulped = true;
                    Blob.NotifyCompanionDrop();
                }
                if (progress >= 1) break;
                await Task.Delay(33, cancellationToken);
            }
        }
        finally
        {
            FileToken.IsVisible = false;
            FileToken.Opacity = 0;
            Blob.State.LookAt(null);
        }
    }

    private async Task AnimateMouthCloseAsync(double target, TimeSpan duration, CancellationToken cancellationToken)
    {
        var start = Blob.MouthOpen;
        const int frames = 6;
        var frameDelay = TimeSpan.FromTicks(duration.Ticks / frames);
        for (var frame = 1; frame <= frames; frame++)
        {
            await Task.Delay(frameDelay, cancellationToken);
            var t = frame / (double)frames;
            Blob.SetMouthOpen(start + (target - start) * t);
        }
    }

    private async Task RelaxAfterFileDragAsync(int sequence, float start)
    {
        if (!Motion.Enabled || _paused)
        {
            Blob.SetMouthOpen(0);
            Blob.SetToneQuiet(BlobatarTone.Idle);
            return;
        }
        const int frames = 6;
        try
        {
            for (var frame = 1; frame <= frames; frame++)
            {
                await Task.Delay(30, _lifetime.Token);
                if (sequence != _dragSequence || _dragOver || _dropPending) return;
                Blob.SetMouthOpen(start * (1 - frame / (double)frames));
            }
            Blob.State.TriggerBlink();
            Blob.SetToneQuiet(BlobatarTone.Idle);
        }
        catch (OperationCanceledException) { }
    }

    private async Task AnimateWheelHandoffAsync(PixelPoint screenPoint)
    {
        if (!Motion.Enabled) return;
        try
        {
            var target = ScreenToClient(screenPoint);
            var centerOnScreen = Blob.PointToScreen(new Point(Blob.Bounds.Width / 2, Blob.Bounds.Height / 2));
            var center = ScreenToClient(centerOnScreen);
            var dx = target.X - center.X;
            var dy = target.Y - center.Y;
            for (var i = 1; i <= 8; i++)
            {
                var t = i / 8d;
                var eased = t * t;
                var scale = 1 - .88 * eased;
                var transform = new TransformGroup();
                transform.Children.Add(new ScaleTransform(scale, scale));
                transform.Children.Add(new TranslateTransform(dx * eased, dy * eased));
                Blob.RenderTransform = transform;
                Blob.Opacity = 1 - .84 * eased;
                await Task.Delay(27, _lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Blob.RenderTransform = null;
            Blob.Opacity = 1;
        }
    }

    private async Task PlayHatchAsync()
    {
        if (!Motion.Enabled) return;
        try
        {
            Blob.Opacity = 0;
            Blob.RenderTransform = new ScaleTransform(.65, .65);
            await Task.Delay(90, _lifetime.Token);
            for (var i = 1; i <= 7; i++)
            {
                if (!IsVisible) return;
                var scale = .65 + .35 * i / 7;
                Blob.RenderTransform = new ScaleTransform(scale, scale);
                Blob.Opacity = i / 7d;
                await Task.Delay(35, _lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Blob.RenderTransform = null;
            Blob.Opacity = 1;
        }
    }

    private Point ScreenToClient(PixelPoint screenPoint)
    {
        var origin = Root.PointToScreen(new Point(0, 0));
        var scale = Math.Max(.1, RenderScaling);
        return new Point((screenPoint.X - origin.X) / scale, (screenPoint.Y - origin.Y) / scale);
    }
}
