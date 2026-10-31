using System;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Filee.App.Controls;
using Xunit;

namespace Filee.App.Tests;

public class BlobatarTests
{
    [Theory]
    [InlineData(BlobatarTone.Idle, 132, 113, 226)]        // #8471E2 (0.71)
    [InlineData(BlobatarTone.Alert, 164, 153, 247)]       // #A499F7 (0.49)
    [InlineData(BlobatarTone.Anticipation, 206, 202, 255)] // #CECAFF (0.10)
    [InlineData(BlobatarTone.Busy, 214, 213, 233)]        // #D6D5E9 (0.28)
    [InlineData(BlobatarTone.Celebration, 209, 206, 255)] // #D1CEFF (0.865)
    public void Colors_match_exact_trait_tones(BlobatarTone tone, byte r, byte g, byte b)
    {
        var color = BlobatarRenderer.ColorForTone(tone);
        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
    }

    [Fact]
    public void Gaze_tracks_cursor_vector_smoothly()
    {
        var state = new BlobatarState();
        // Mouse to the right (+500, 0)
        state.LookAt(new Point(500, 0));

        // Advance several frames
        for (var i = 0; i < 30; i++)
            state.Update(1 / 60.0);

        Assert.True(state.GazeX > 2.0, "Gaze should move positively along X towards cursor");
        Assert.True(Math.Abs(state.GazeY) < 0.2, "Gaze Y should stay near center when mouse is purely horizontal");
    }

    [Fact]
    public void Gaze_clamps_maximum_travel()
    {
        var state = new BlobatarState();
        // Huge distance
        state.LookAt(new Point(10000, 0));
        for (var i = 0; i < 60; i++)
            state.Update(1 / 60.0);

        Assert.InRange(state.GazeX, 4.0, 5.0);
    }

    [Fact]
    public void Gaze_resets_to_center_when_looking_at_origin()
    {
        var state = new BlobatarState();
        state.LookAt(new Point(300, 200));
        for (var i = 0; i < 30; i++)
            state.Update(1 / 60.0);

        state.LookAt(new Point(0, 0));
        for (var i = 0; i < 40; i++)
            state.Update(1 / 60.0);

        Assert.True(Math.Abs(state.GazeX) < 0.05);
        Assert.True(Math.Abs(state.GazeY) < 0.05);
    }

    [Fact]
    public void Blinking_squashes_vertical_eye_scale()
    {
        var state = new BlobatarState();
        state.TriggerBlink();
        // At mid-blink (~65ms) eye scale should drop
        state.Update(0.065);
        Assert.True(state.BlinkScaleY < 0.4, $"Blink scale should be squashed: {state.BlinkScaleY}");

        // At end of blink (~150ms) eye scale should recover to 1.0
        state.Update(0.10);
        Assert.True(state.BlinkScaleY > 0.9, $"Blink scale should recover: {state.BlinkScaleY}");
    }

    [Fact]
    public void Squish_bounce_recovers_elastically()
    {
        var state = new BlobatarState();
        state.TriggerBounce(0.5);
        state.Update(0.03);
        // During squish bounce, ScaleY differs from 1.0
        Assert.NotEqual(1.0, state.ScaleY);

        // After settling time (0.5s)
        for (var i = 0; i < 40; i++)
            state.Update(1 / 60.0);

        Assert.InRange(state.ScaleY, 0.95, 1.05);
    }

    [AvaloniaFact]
    public void Donut_menu_shows_mascot_by_default()
    {
        var donut = new DonutMenu();
        Assert.True(donut.ShowCenterMascot);
    }
}
