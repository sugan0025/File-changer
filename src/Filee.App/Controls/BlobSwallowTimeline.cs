namespace Filee.App.Controls;

/// <summary>Deterministic keyframe curve for the companion's visual file swallow.</summary>
internal readonly record struct BlobSwallowFrame(
    double TokenProgress,
    double TokenScale,
    double TokenOpacity,
    double MouthOpen,
    double EyeBlink,
    bool TokenVisible,
    bool Gulp);

internal static class BlobSwallowTimeline
{
    // Normalized keyframes keep the animation testable without coupling it to a window timer.
    public static BlobSwallowFrame At(double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        var flight = Ease(Math.Clamp(t / .62, 0, 1));
        var mouth = t < .62
            ? .82 + .18 * Ease(t / .62)
            : t < .84
                ? 1 - .88 * Ease((t - .62) / .22)
                : .12 * (1 - Ease((t - .84) / .16));
        var tokenVisible = t < .72;
        // One synchronized blink is used for both eyes before the gulp; directional
        // gaze flicks can look like a one-eye twitch because the eyes have different limits.
        var twitch = t is >= .66 and < .82
            ? Math.Sin(Math.Clamp((t - .66) / .16, 0, 1) * Math.PI)
            : 0;
        var tokenScale = 1 - .68 * flight;
        var tokenOpacity = t < .62 ? 1 - .18 * flight : 1 - Ease((t - .62) / .10);
        return new BlobSwallowFrame(flight, tokenScale, Math.Clamp(tokenOpacity, 0, 1), Math.Clamp(mouth, 0, 1),
            Math.Clamp(twitch, -1, 1), tokenVisible, t >= .84);
    }

    private static double Ease(double value) => value * value * (3 - 2 * value);
}
