using System.Numerics;

namespace Filee.App.Services;

internal enum BlobAppBodyMotion { None, Scan, Think, Orbit, Proud, Settle, Sway, DoubleBlink }
internal enum BlobCompanionMoment { Idle, AppReaction, UserReaction, Conversion, FileDrop }

internal readonly record struct BlobReemergenceFrame(double Scale, double Opacity);

/// <summary>Pure companion-only behavior rules, kept separate from the shared wheel Blob.</summary>
internal static class BlobCompanionBehavior
{
    public static BlobAppBodyMotion MotionForApp(string? key) => key switch
    {
        "claude" or "chatgpt" or "gemini" => BlobAppBodyMotion.Think,
        "antigravity" => BlobAppBodyMotion.Orbit,
        "chrome" or "file-explorer" => BlobAppBodyMotion.Scan,
        "linkedin" => BlobAppBodyMotion.Proud,
        "netflix" => BlobAppBodyMotion.Settle,
        "spotify" => BlobAppBodyMotion.Sway,
        "whatsapp" => BlobAppBodyMotion.DoubleBlink,
        _ => BlobAppBodyMotion.None,
    };

    public static bool CanInterrupt(BlobCompanionMoment current, BlobCompanionMoment requested) => requested >= current;

    public static bool CanWakeForFiles(bool paused, int fileCount) => !paused && fileCount > 0;

    public static bool HugCompleted(bool held, bool moved, bool controlHeld, bool paused, bool gestureActive) =>
        held && !moved && !controlHeld && !paused && !gestureActive;

    public static bool NapExpired(DateTime? napUntil, DateTime now) => napUntil is { } due && now >= due;

    public static BlobReemergenceFrame ReemergenceAt(double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        const double overshootAt = .78;
        var scale = p <= overshootAt
            ? .14 + 1.02 * EaseOut(p / overshootAt)
            : 1.16 - .16 * EaseOut((p - overshootAt) / (1 - overshootAt));
        return new BlobReemergenceFrame(scale, EaseOut(p));
    }

    public static double PerchLean(int windowX, int windowWidth, int screenX, int screenWidth, double scale)
    {
        var margin = Math.Max(1, 14 * scale);
        if (windowX - screenX <= margin) return -6;
        if (screenX + screenWidth - (windowX + windowWidth) <= margin) return 6;
        return 0;
    }

    public static float Fullness(int swallowedFiles, TimeSpan sinceLastSwallow)
    {
        if (swallowedFiles <= 0 || sinceLastSwallow < TimeSpan.Zero) return 0;
        var remaining = Math.Clamp(swallowedFiles - sinceLastSwallow.TotalMinutes / 20.0, 0, 5);
        return (float)(remaining / 5.0);
    }

    private static double EaseOut(double value)
    {
        var inverse = 1 - Math.Clamp(value, 0, 1);
        return 1 - inverse * inverse;
    }
}

internal sealed class BlobDragYankDetector
{
    private Vector2 _lastDirection;
    private DateTime _windowStarted;
    private int _reversals;

    public bool IsYank(DateTime now) => _reversals >= 4 && now - _windowStarted <= TimeSpan.FromSeconds(1);

    public void Record(double dx, double dy, DateTime now)
    {
        var delta = new Vector2((float)dx, (float)dy);
        if (delta.LengthSquared() < 9) return;
        if (_windowStarted == default || now - _windowStarted > TimeSpan.FromSeconds(1))
        {
            _windowStarted = now;
            _reversals = 0;
            _lastDirection = Vector2.Normalize(delta);
            return;
        }

        var direction = Vector2.Normalize(delta);
        if (Vector2.Dot(direction, _lastDirection) < -.35f)
            _reversals++;
        _lastDirection = direction;
    }

    public void Reset()
    {
        _lastDirection = Vector2.Zero;
        _windowStarted = default;
        _reversals = 0;
    }
}

internal sealed class StretchReminderSchedule
{
    private DateTime? _activeSince;
    private DateTime? _lastReminder;

    public bool Update(TimeSpan? systemIdle, DateTime now, bool enabled, bool canShow)
    {
        if (!enabled || systemIdle is not { } idle || idle < TimeSpan.Zero || idle > TimeSpan.FromSeconds(30))
        {
            _activeSince = null;
            return false;
        }

        _activeSince ??= now - idle;
        if (now - _activeSince.Value < TimeSpan.FromMinutes(60)
            || _lastReminder is { } last && now - last < TimeSpan.FromMinutes(90)
            || !canShow)
            return false;

        _lastReminder = now;
        _activeSince = now;
        return true;
    }
}
