using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Filee.App.Services.Triggers;
using Filee.App.Views;
using Filee.App.Controls;
using Filee.Core.Platform;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Filee.App.Services;

/// <summary>Owns the optional Windows desktop window that hosts the existing Blobatar.</summary>
public sealed class BlobCompanionService(UserDataStore store, RadialController radial, TriggerService triggers, ConversionService conversions,
    IPlatformServices platform, ILogger<BlobCompanionService> log) : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan SelectionSettleDelay = TimeSpan.FromMilliseconds(350);
    private BlobCompanionWindow? _window;
    private DispatcherTimer? _saveTimer;
    private DispatcherTimer? _awarenessTimer;
    private DispatcherTimer? _idleTimer;
    private DispatcherTimer? _cinemaTimer;
    private PixelPoint? _pendingPosition;
    private IDisposable? _foregroundChanges;
    private IDisposable? _focusedInputWatcher;
    private readonly BlobQuipPicker _quipPicker = new();
    private readonly ForegroundActivationTracker _foregroundTracker = new();
    private readonly FileSelectionDebouncer _selectionDebouncer = new(SelectionSettleDelay);
    private bool _isExplorerOrDesktopForeground;
    private bool _hasSelectionBaseline;
    private bool _disposed;
    private bool _gestureActive;
    private bool _settingsSubscribed;
    private bool _conversionSubscribed;

    public bool IsSupported => OperatingSystem.IsWindows();
    public bool IsVisible => _window?.IsVisible == true;
    public bool IsEnabled => store.Settings.BlobCompanion.IsVisible;

    public void Start()
    {
        if (!_settingsSubscribed)
        {
            store.SettingsChanged += OnSettingsChanged;
            _settingsSubscribed = true;
        }
        if (!_conversionSubscribed)
        {
            conversions.JobProgressChanged += OnJobProgressChanged;
            conversions.JobFinishedOnUi += OnJobFinished;
            _conversionSubscribed = true;
        }
        if (!IsSupported || !store.Settings.BlobCompanion.IsVisible)
            return;
        Show();
    }

    public void SetPaused(bool paused)
    {
        if (_window is not null)
            _window.IsPaused = paused;
        SyncIdleMonitor();
        SyncAwarenessMonitor();
        SyncPresentationMonitor();
    }

    public void SetGestureActive(bool active)
    {
        _gestureActive = active;
        if (_window is not null)
            _window.SetGestureActive(active);
        SyncAwarenessMonitor();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnSettingsChanged(sender, e));
            return;
        }
        SetPaused(store.Settings.Paused);
        if (!store.Settings.BlobCompanion.IsVisible)
            Hide();
        else if (!radial.IsVisible && !IsVisible)
            Show();
        else if (radial.IsVisible && store.Settings.BlobCompanion.IsVisible)
            _window?.WaitForWheelClose();
        if (_window is not null)
        {
            _window.Activity = store.Settings.BlobCompanion.Activity;
            _window.CompanionAudioEnabled = store.Settings.BlobCompanion.AudioEnabled;
            _window.StretchReminderEnabled = store.Settings.BlobCompanion.StretchReminderEnabled;
            _window.IsCompanionEnabled = () => store.Settings.BlobCompanion.IsVisible;
        }
        SyncAwarenessMonitor();
        SyncIdleMonitor();
        SyncPresentationMonitor();
    }

    private void OnJobProgressChanged(object? sender, Filee.Core.Conversion.ConversionJob job)
    {
        if (job.State == Filee.Core.Conversion.JobState.Running)
            _window?.ShowConversionProgress(job.Progress);
    }

    private void OnJobFinished(object? sender, Filee.Core.Conversion.ConversionJob job) => _window?.ShowConversionResult(job);

    public void SetVisible(bool visible)
    {
        if (!IsSupported || _disposed)
            return;

        store.Settings.BlobCompanion.IsVisible = visible;
        if (visible)
            Show();
        else
            Hide();

        try
        {
            store.SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Could not save the Blob companion visibility");
        }

    }

    public void ToggleVisible() => SetVisible(!store.Settings.BlobCompanion.IsVisible);

    public void NapFor(TimeSpan duration) => _window?.NapFor(duration);
    public void QuietAppQuipsFor(TimeSpan duration) => _window?.QuietAppQuipsFor(duration);

    private void Show()
    {
        if (_disposed || !IsSupported)
            return;
        if (!store.Settings.BlobCompanion.IsVisible)
            return;
        if (_window is null)
        {
            var settings = store.Settings.BlobCompanion;
            _window = new BlobCompanionWindow(radial, triggers)
            {
                IsPaused = store.Settings.Paused,
                GestureActive = _gestureActive,
                Activity = settings.Activity,
                CompanionAudioEnabled = settings.AudioEnabled,
                StretchReminderEnabled = settings.StretchReminderEnabled,
                IsCompanionEnabled = () => store.Settings.BlobCompanion.IsVisible,
                PlayHatchOnNextShow = !settings.HatchSeen,
            };
            if (!settings.HatchSeen)
            {
                settings.HatchSeen = true;
                try { store.SaveSettings(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log.LogWarning(ex, "Could not save the Blob companion introduction state");
                }
            }
            _window.PositionMoved += OnPositionMoved;
            _window.VisibilityChanged += OnCompanionVisibilityChanged;
            _window.Closed += (_, _) => _window = null;
            Place(_window, store.Settings.BlobCompanion);
        }

        if (radial.IsVisible)
        {
            _window.WaitForWheelClose();
            return;
        }

        if (!_window.IsVisible)
            _window.Show();
        SyncAwarenessMonitor();
        SyncIdleMonitor();
        SyncPresentationMonitor();
    }

    private void OnCompanionVisibilityChanged(bool visible)
    {
        SyncAwarenessMonitor();
        SyncIdleMonitor();
        SyncPresentationMonitor();
    }

    private void SyncIdleMonitor()
    {
        var shouldTrack = !_disposed && IsSupported && store.Settings.BlobCompanion.IsVisible
            && !store.Settings.Paused && _window?.IsVisible == true;
        if (!shouldTrack)
        {
            _idleTimer?.Stop();
            return;
        }
        _idleTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idleTimer.Tick -= OnIdleTick;
        _idleTimer.Tick += OnIdleTick;
        if (!_idleTimer.IsEnabled)
            _idleTimer.Start();
        OnIdleTick(this, EventArgs.Empty);
    }

    private void SyncPresentationMonitor()
    {
        var shouldCheck = !_disposed && IsSupported && store.Settings.BlobCompanion.IsVisible
            && !store.Settings.Paused && store.Settings.BlobCompanion.CinemaDimEnabled && _window?.IsVisible == true;
        if (!shouldCheck)
        {
            _cinemaTimer?.Stop();
            _window?.SetCinemaDimState(false);
            return;
        }

        _cinemaTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _cinemaTimer.Tick -= OnPresentationTick;
        _cinemaTimer.Tick += OnPresentationTick;
        if (!_cinemaTimer.IsEnabled)
            _cinemaTimer.Start();
        OnPresentationTick(this, EventArgs.Empty);
    }

    private void OnPresentationTick(object? sender, EventArgs e)
    {
        if (_window?.IsVisible != true || store.Settings.Paused || !store.Settings.BlobCompanion.CinemaDimEnabled)
        {
            SyncPresentationMonitor();
            return;
        }

        bool active;
        try { active = platform.IsPresentationModeActive(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            active = false;
        }
        if (_window.SetCinemaDimState(active))
            SyncAwarenessMonitor();
    }

    private void OnIdleTick(object? sender, EventArgs e)
    {
        if (_window?.IsVisible != true || store.Settings.Paused)
        {
            SyncIdleMonitor();
            return;
        }
        try { _window.SetSystemIdleTime(platform.GetSystemIdleTime()); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }
    }

    private void SyncAwarenessMonitor()
    {
        var shouldObserve = !_disposed && IsSupported && store.Settings.BlobCompanion.IsVisible
            && store.Settings.BlobCompanion.AppAwareness.Enabled && !store.Settings.Paused
            && _window?.CanObserveAppContext == true && !radial.IsVisible;
        if (!shouldObserve)
        {
            StopAwarenessObservers();
            return;
        }

        _awarenessTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _awarenessTimer.Tick -= OnAwarenessTick;
        _awarenessTimer.Tick += OnAwarenessTick;
        if (_awarenessTimer.IsEnabled)
            return;

        // Establish a baseline first so enabling awareness never greets the app already in front.
        var current = SafeForegroundContext();
        _foregroundTracker.EstablishBaseline(current);
        _isExplorerOrDesktopForeground = BlobAppAwareness.IsExplorerOrDesktop(current);
        EstablishSelectionBaseline(current);
        _foregroundChanges = platform.WatchForegroundChanges(context =>
            Dispatcher.UIThread.Post(() => HandleForegroundEntry(context)));
        _focusedInputWatcher = platform.WatchFocusedInputBounds(bounds =>
            Dispatcher.UIThread.Post(() => _window?.SetFocusedInputBounds(bounds)));
        _awarenessTimer.Start();
    }

    private void OnAwarenessTick(object? sender, EventArgs e)
    {
        if (_window?.CanObserveAppContext != true || radial.IsVisible || store.Settings.Paused
            || !store.Settings.BlobCompanion.IsVisible || !store.Settings.BlobCompanion.AppAwareness.Enabled)
        {
            SyncAwarenessMonitor();
            return;
        }

        var context = SafeForegroundContext();
        HandleForegroundEntry(context);
        if (_isExplorerOrDesktopForeground && IsExplorerSelectionEnabled())
        {
            try
            {
                if (!_hasSelectionBaseline)
                    EstablishSelectionBaseline(context);
                else if (_selectionDebouncer.Update(platform.GetFileManagerSelection(), DateTime.UtcNow) is { } count)
                {
                    var key = count == 1 ? "file-selection-single" : "file-selection-multiple";
                    var pool = count == 1 ? BlobAppAwareness.SingleFileQuips : BlobAppAwareness.MultipleFileQuips;
                    if (_quipPicker.Next(key, pool) is { } quip)
                        _window.ShowAppReaction(quip);
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
            {
                _selectionDebouncer.Reset();
                _hasSelectionBaseline = false;
            }
        }
        else
        {
            _selectionDebouncer.Reset();
            _hasSelectionBaseline = false;
        }
    }

    private void HandleForegroundEntry(ForegroundAppContext? context)
    {
        if (_window?.CanObserveAppContext != true || radial.IsVisible || store.Settings.Paused
            || !store.Settings.BlobCompanion.IsVisible || !store.Settings.BlobCompanion.AppAwareness.Enabled)
            return;

        var changed = _foregroundTracker.Enter(context, out var appKey);
        var isExplorerOrDesktop = BlobAppAwareness.IsExplorerOrDesktop(context);
        if (changed)
        {
            _isExplorerOrDesktopForeground = isExplorerOrDesktop;
            if (isExplorerOrDesktop && IsExplorerSelectionEnabled())
                EstablishSelectionBaseline(context);
            else
            {
                _selectionDebouncer.Reset();
                _hasSelectionBaseline = false;
            }

            if (BlobAppAwareness.ForKey(appKey) is { } reaction
                && store.Settings.BlobCompanion.AppAwareness.EnabledApps?.Contains(reaction.Key, StringComparer.OrdinalIgnoreCase) == true
                && _quipPicker.Next(reaction.Key, reaction.Quips) is { } quip)
                _window.ShowAppReaction(quip, reaction.Key);
        }
        else
        {
            _isExplorerOrDesktopForeground = isExplorerOrDesktop;
        }
    }

    private bool IsExplorerSelectionEnabled() =>
        store.Settings.BlobCompanion.AppAwareness.EnabledApps?.Contains("file-explorer", StringComparer.OrdinalIgnoreCase) == true;

    private void EstablishSelectionBaseline(ForegroundAppContext? context)
    {
        _selectionDebouncer.Reset();
        _hasSelectionBaseline = false;
        if (!BlobAppAwareness.IsExplorerOrDesktop(context) || !IsExplorerSelectionEnabled())
            return;

        try
        {
            _selectionDebouncer.EstablishBaseline(platform.GetFileManagerSelection(), DateTime.UtcNow);
            _hasSelectionBaseline = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _selectionDebouncer.Reset();
        }
    }

    private void StopAwarenessObservers()
    {
        _awarenessTimer?.Stop();
        _foregroundChanges?.Dispose();
        _foregroundChanges = null;
        _focusedInputWatcher?.Dispose();
        _focusedInputWatcher = null;
        _foregroundTracker.Reset();
        _isExplorerOrDesktopForeground = false;
        _selectionDebouncer.Reset();
        _hasSelectionBaseline = false;
        _window?.SetFocusedInputBounds(null);
    }

    private ForegroundAppContext? SafeForegroundContext()
    {
        try { return platform.GetForegroundAppContext(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Place(BlobCompanionWindow window, BlobCompanionSettings settings)
    {
        var size = new Size(window.Width, window.Height);
        var saved = settings.X is { } x && settings.Y is { } y ? new PixelPoint(x, y) : (PixelPoint?)null;
        var screen = saved is { } point ? window.Screens.ScreenFromPoint(point) : null;
        screen ??= window.Screens.Primary;

        if (screen is null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)Math.Ceiling(size.Width * scale);
        var height = (int)Math.Ceiling(size.Height * scale);
        var position = saved ?? new PixelPoint(area.Right - width - 32, area.Bottom - height - 48);
        window.Position = new PixelPoint(
            Math.Clamp(position.X, area.X, Math.Max(area.X, area.Right - width)),
            Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
    }

    private void OnPositionMoved(object? sender, PixelPoint position)
    {
        _pendingPosition = position;
        _saveTimer ??= new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick -= SavePendingPosition;
        _saveTimer.Tick += SavePendingPosition;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SavePendingPosition(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        if (_pendingPosition is not { } position)
            return;
        _pendingPosition = null;
        try
        {
            store.SaveBlobCompanionPosition(position.X, position.Y);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Could not remember the Blob companion position");
        }
    }

    private void Hide()
    {
        SavePendingPosition(this, EventArgs.Empty);
        _window?.Hide();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_settingsSubscribed)
        {
            store.SettingsChanged -= OnSettingsChanged;
            _settingsSubscribed = false;
        }
        if (_conversionSubscribed)
        {
            conversions.JobProgressChanged -= OnJobProgressChanged;
            conversions.JobFinishedOnUi -= OnJobFinished;
            _conversionSubscribed = false;
        }
        StopAwarenessObservers();
        _awarenessTimer = null;
        _idleTimer?.Stop();
        _idleTimer = null;
        _cinemaTimer?.Stop();
        _cinemaTimer = null;
        _quipPicker.Clear();
        SavePendingPosition(this, EventArgs.Empty);
        _saveTimer?.Stop();
        _saveTimer = null;
        if (_window is { } window)
        {
            window.SetCinemaDimState(false);
            window.PositionMoved -= OnPositionMoved;
            window.VisibilityChanged -= OnCompanionVisibilityChanged;
            window.Close();
            _window = null;
        }
    }
}
