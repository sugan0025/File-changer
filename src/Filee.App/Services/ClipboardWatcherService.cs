using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Filee.App.Services;

public sealed class ClipboardWatcherService(
    UserDataStore store,
    IPlatformServices platform,
    RadialController radial,
    WindowService windows,
    ILocalizer? loc = null,
    ILogger<ClipboardWatcherService>? log = null) : IDisposable
{
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private DispatcherTimer? _timer;
    private uint _lastSequence;
    private string? _lastSignature;

    public void Start()
    {
        if (_timer is not null)
            return;

        // Clipboard watcher is supported on Windows and macOS
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            return;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                _lastSequence = GetClipboardSequenceNumber();
            }
            catch
            {
            }
        }

        var interval = OperatingSystem.IsWindows() ? TimeSpan.FromMilliseconds(800) : TimeSpan.FromMilliseconds(1200);
        _timer = new DispatcherTimer(interval, DispatcherPriority.Background, OnTick);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!store.Settings.ClipboardAutoDetect || store.Settings.Paused)
            return;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                uint currentSeq = GetClipboardSequenceNumber();
                if (currentSeq == _lastSequence || currentSeq == 0)
                    return;
                _lastSequence = currentSeq;
            }
            catch
            {
            }
        }

        ClipboardContent? content;
        try
        {
            content = platform.ReadClipboard();
        }
        catch (Exception ex)
        {
            log?.LogDebug(ex, "Could not check clipboard content");
            return;
        }

        if (content is null)
            return;

        if (content.Files.Count > 0)
        {
            var sig = "files:" + string.Join(";", content.Files);
            if (sig == _lastSignature)
                return;
            _lastSignature = sig;

            var title = loc?["clipboard.files.title"] is { Length: > 0 } t ? t : "Clipboard files detected";
            var desc = loc?["clipboard.files.desc"] is { Length: > 0 } d ? string.Format(d, content.Files.Count) : $"{content.Files.Count} file(s) on clipboard. Convert with Filee?";
            windows.ShowClipboardNotice(title, desc, () =>
            {
                var pt = ScreenCenter();
                radial.ShowForFiles(pt.X, pt.Y, content.Files, toClipboard: true);
            });
        }
        else if (content.Picture is { Length: > 0 } pic)
        {
            var sample = pic.Length > 32 ? $"{pic[0]:X2}{pic[pic.Length / 2]:X2}{pic[^1]:X2}" : Convert.ToHexString(pic);
            var sig = $"pic:{pic.Length}:{sample}";
            if (sig == _lastSignature)
                return;
            _lastSignature = sig;

            var title = loc?["clipboard.image.title"] is { Length: > 0 } t ? t : "Clipboard image detected";
            var desc = loc?["clipboard.image.desc"] is { Length: > 0 } d ? d : "New image copied. Convert with Filee?";
            windows.ShowClipboardNotice(title, desc, () =>
            {
                var name = $"Clipboard {DateTime.Now:yyyy-MM-dd HH.mm.ss}.{content.PictureExtension ?? "png"}";
                var web = new Filee.Core.Web.WebDrop([Filee.Core.Web.WebDropItem.FromBytes(name, pic)], Filee.Core.Web.WebDrop.DefaultWorkRoot)
                {
                    SaveOnlyConverted = true,
                };
                var pt = ScreenCenter();
                radial.ShowForFiles(pt.X, pt.Y, web.Placeholders, web: web, toClipboard: true);
            });
        }
        else
        {
            _lastSignature = null;
        }
    }

    private PixelPoint ScreenCenter()
    {
        var screen = windows.Main?.Screens.Primary;
        return screen?.WorkingArea.Center ?? new PixelPoint(500, 400);
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
    }
}
