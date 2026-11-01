using System;

namespace Filee.Core.Settings;

public enum CircadianMode
{
    Off,
    Clock,
    Sun,
    Pinned,
}

public enum CircadianPhase
{
    Morning,
    Afternoon,
    Night,
}

public sealed class CircadianSettings
{
    public CircadianMode Mode { get; set; } = CircadianMode.Off;
    public TimeOnly MorningStart { get; set; } = new(6, 0);
    public TimeOnly AfternoonStart { get; set; } = new(12, 0);
    public TimeOnly NightStart { get; set; } = new(18, 0);
    public int TransitionMinutes { get; set; } = 60;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public CircadianPhase? Pinned { get; set; }
    public DateTimeOffset? PinnedUntil { get; set; }

    public CircadianSettings Clone() => (CircadianSettings)MemberwiseClone();
}
