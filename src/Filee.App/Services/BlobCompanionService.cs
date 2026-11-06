using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Filee.App.Views;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Filee.App.Services;

/// <summary>Owns the optional Windows desktop window that hosts the existing Blobatar.</summary>
public sealed class BlobCompanionService(UserDataStore store, ILogger<BlobCompanionService> log) : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(350);
    private BlobCompanionWindow? _window;
    private DispatcherTimer? _saveTimer;
    private PixelPoint? _pendingPosition;
    private bool _disposed;

    public bool IsSupported => OperatingSystem.IsWindows();
    public bool IsVisible => _window?.IsVisible == true;

    public void Start()
    {
        if (!IsSupported || !store.Settings.BlobCompanion.IsVisible)
            return;
        Show();
    }

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

    public void ToggleVisible() => SetVisible(!IsVisible);

    private void Show()
    {
        if (_disposed || !IsSupported)
            return;
        if (_window is null)
        {
            _window = new BlobCompanionWindow();
            _window.PositionMoved += OnPositionMoved;
            _window.Closed += (_, _) => _window = null;
            Place(_window, store.Settings.BlobCompanion);
        }

        if (!_window.IsVisible)
            _window.Show();
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
        SavePendingPosition(this, EventArgs.Empty);
        _saveTimer?.Stop();
        _saveTimer = null;
        if (_window is { } window)
        {
            window.PositionMoved -= OnPositionMoved;
            window.Close();
            _window = null;
        }
    }
}
