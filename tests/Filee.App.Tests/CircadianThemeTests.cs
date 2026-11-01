using System;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Filee.App.Services;
using Filee.Core.Settings;
using Xunit;

namespace Filee.App.Tests;

public class CircadianThemeTests
{
    [Fact]
    public void OkLab_conversions_and_lerp_are_accurate()
    {
        var white = Colors.White;
        var black = Colors.Black;
        var labWhite = OkLab.FromColor(white);
        Assert.True(labWhite.L > 0.99f);

        var labBlack = OkLab.FromColor(black);
        Assert.True(labBlack.L < 0.01f);

        // Midpoint lerp should produce smooth intermediate color
        var mid = OkLab.Lerp(Colors.Red, Colors.Blue, 0.5);
        Assert.True(mid.R > 0 && mid.B > 0);

        // sRGB roundtrip
        var original = Color.FromArgb(255, 120, 180, 240);
        var lab = OkLab.FromColor(original);
        var restored = lab.ToColor();
        Assert.InRange(Math.Abs(restored.R - original.R), 0, 2);
        Assert.InRange(Math.Abs(restored.G - original.G), 0, 2);
        Assert.InRange(Math.Abs(restored.B - original.B), 0, 2);
    }

    [Fact]
    public void SunCalc_computes_reasonable_solar_times()
    {
        // Equinox in London
        var date = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc);
        var times = SunCalc.Calculate(date, 51.5074, -0.1278, TimeSpan.Zero);

        // Sunrise should be around 06:00 UTC (±60 min)
        Assert.InRange(times.Sunrise.Hour, 5, 7);
        // Solar noon should be around 12:00 UTC (±60 min)
        Assert.InRange(times.SolarNoon.Hour, 11, 13);
        // Sunset should be around 18:00 UTC (±60 min)
        Assert.InRange(times.Sunset.Hour, 17, 19);
    }

    [Fact]
    public void CircadianService_resolves_phases_correctly()
    {
        var service = new CircadianService();
        var resources = new ResourceDictionary();
        service.Attach(resources, null!);

        var settings = new CircadianSettings
        {
            Mode = CircadianMode.Clock,
            MorningStart = new TimeOnly(6, 0),
            AfternoonStart = new TimeOnly(12, 0),
            NightStart = new TimeOnly(18, 0),
            TransitionMinutes = 30
        };
        service.Configure(settings);

        // Preview at 09:00 (Morning)
        service.SetPreviewMinute(9 * 60);
        Assert.Equal(CircadianPhase.Morning, service.CurrentPhase);
        Assert.NotNull(resources["PhaseScrimBrush"]);
        Assert.NotNull(resources["GlassCardBrush"]);

        // Preview at 14:00 (Afternoon)
        service.SetPreviewMinute(14 * 60);
        Assert.Equal(CircadianPhase.Afternoon, service.CurrentPhase);

        // Preview at 22:00 (Night)
        service.SetPreviewMinute(22 * 60);
        Assert.Equal(CircadianPhase.Night, service.CurrentPhase);

        // Test Pinned mode
        settings.Mode = CircadianMode.Pinned;
        settings.Pinned = CircadianPhase.Morning;
        service.SetPreviewMinute(null);
        service.Configure(settings);
        Assert.Equal(CircadianPhase.Morning, service.CurrentPhase);

        settings.Pinned = CircadianPhase.Afternoon;
        service.Configure(settings);
        Assert.Equal(CircadianPhase.Afternoon, service.CurrentPhase);
    }

    [Fact]
    public void CircadianSettings_serializes_and_deserializes()
    {
        var original = new CircadianSettings
        {
            Mode = CircadianMode.Sun,
            MorningStart = new TimeOnly(7, 15),
            AfternoonStart = new TimeOnly(13, 0),
            NightStart = new TimeOnly(19, 45),
            TransitionMinutes = 45,
            Latitude = 37.7749,
            Longitude = -122.4194,
            Pinned = CircadianPhase.Afternoon
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<CircadianSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Mode, restored.Mode);
        Assert.Equal(original.MorningStart, restored.MorningStart);
        Assert.Equal(original.AfternoonStart, restored.AfternoonStart);
        Assert.Equal(original.NightStart, restored.NightStart);
        Assert.Equal(original.TransitionMinutes, restored.TransitionMinutes);
        Assert.Equal(original.Latitude, restored.Latitude);
        Assert.Equal(original.Longitude, restored.Longitude);
        Assert.Equal(original.Pinned, restored.Pinned);
    }
}
