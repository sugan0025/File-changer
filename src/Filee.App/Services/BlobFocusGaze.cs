using Avalonia;
using Filee.App.Controls;
using Filee.Core.Platform;

namespace Filee.App.Services;

internal static class BlobFocusGaze
{
    private static readonly TimeSpan RecentInputWindow = TimeSpan.FromSeconds(3);

    public static bool HasRecentInput(TimeSpan? idle) => idle is { } elapsed
        && elapsed >= TimeSpan.Zero && elapsed <= RecentInputWindow;

    public static Point? ToViewBoxTarget(PlatformScreenBounds? bounds, PixelPoint blobOrigin, double renderScaling, Rect blobBounds)
    {
        if (bounds is not { Width: > 0, Height: > 0 } focused)
            return null;

        var centerX = focused.X + focused.Width / 2d;
        var centerY = focused.Y + focused.Height / 2d;
        var scale = Math.Max(.1, renderScaling);
        var local = new Point((centerX - blobOrigin.X) / scale, (centerY - blobOrigin.Y) / scale);
        return BlobatarRenderer.ToViewBox(local, blobBounds);
    }
}
