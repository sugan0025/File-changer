using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.Services.Triggers;
using Filee.App.Views;
using Xunit;

namespace Filee.App.Tests;

[Collection("Motion")]
public class BlobatarTests
{
    [AvaloniaFact]
    public void Companion_window_loads_the_existing_blob_control()
    {
        TestServices.EnsureInitialized();
        var window = new BlobCompanionWindow(AppHost.Get<RadialController>(), AppHost.Get<TriggerService>());
        try
        {
            Assert.Equal(104, window.Width);
            var root = Assert.IsType<Grid>(window.Content);
            Assert.Contains(root.Children, control => control is BlobatarControl { Name: "Blob" });
        }
        finally
        {
            window.Close();
        }
    }

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

        Assert.True(Math.Abs(state.GazeX) < 0.4);
        Assert.True(Math.Abs(state.GazeY) < 0.4);
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
    public void Paused_and_napping_companion_states_hold_the_sleepy_pose()
    {
        var state = new BlobatarState();
        state.SetPaused(true);
        state.Update(0.08);
        Assert.InRange(state.Frame.Sleepy, 0.95f, 1f);

        state.SetPaused(false);
        state.SetNapping(true);
        state.Update(0.08);
        Assert.InRange(state.Frame.Sleepy, 0.95f, 1f);
    }

    [Fact]
    public void Bored_hop_uses_a_small_ballistic_jump()
    {
        var state = new BlobatarState();
        state.TriggerBoredHop();
        state.Update(0.05);
        Assert.InRange(state.FloatY, 0.5, 5.0);
    }

    [Fact]
    public void Companion_swallow_does_not_change_the_radial_blobs_legacy_drop_hop()
    {
        var wheelBlob = new BlobatarState();
        wheelBlob.Drop();
        for (var i = 0; i < 18; i++) wheelBlob.Update(1 / 60.0);
        Assert.True(wheelBlob.FloatY > 0, "The radial Blob's legacy gulp-and-hop must remain intact.");

        var companion = new BlobatarState { CompanionMode = true };
        companion.CompanionDrop();
        for (var i = 0; i < 30; i++) companion.Update(1 / 60.0);
        Assert.Equal(0, companion.FloatY);
        Assert.NotEqual(1, companion.ScaleY);
    }

    [Fact]
    public void Companion_swallow_keyframes_start_flight_gulp_and_settle_in_order()
    {
        var start = BlobSwallowTimeline.At(0);
        var flight = BlobSwallowTimeline.At(.62);
        var twitchLeft = BlobSwallowTimeline.At(.68);
        var twitchRight = BlobSwallowTimeline.At(.78);
        var gulp = BlobSwallowTimeline.At(.84);
        var settled = BlobSwallowTimeline.At(1);

        Assert.Equal(0, start.TokenProgress);
        Assert.Equal(1, start.TokenScale);
        Assert.True(flight.TokenVisible);
        Assert.Equal(1, flight.TokenProgress);
        Assert.NotEqual(0, twitchLeft.EyeBlink);
        Assert.True(twitchLeft.EyeBlink > 0 && twitchRight.EyeBlink > 0,
            "The synchronized eye reaction should be active before the gulp without sending either eye in a separate direction.");
        Assert.False(twitchLeft.Gulp);
        Assert.True(gulp.Gulp);
        Assert.True(gulp.MouthOpen > 0);
        Assert.False(settled.TokenVisible);
        Assert.Equal(0, settled.MouthOpen);
    }

    [AvaloniaFact]
    public void Windows_extension_icon_provider_uses_the_shell_association()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var icon = WindowsFileTypeIconProvider.TryGet(".pdf");
        Assert.NotNull(icon);
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

    [Fact]
    public void Blob_stays_still_while_eyes_track_gaze()
    {
        var state = new BlobatarState();
        state.LookAt(new Point(600, 0));
        for (var i = 0; i < 30; i++)
            state.Update(1 / 60.0);

        // Eyes pursue the target smoothly
        Assert.True(state.GazeX > 2.0, $"Eyes should gaze right: {state.GazeX}");
        Assert.InRange(state.ScaleX, 0.97, 1.03);
        Assert.InRange(state.ScaleY, 0.97, 1.03);
        Assert.Equal(0.0, state.TiltAngle);
        Assert.Equal(0.0, state.FloatY);
    }

    [Fact]
    public void Tilt_wobble_animates_on_trigger()
    {
        var state = new BlobatarState();
        state.TriggerTiltWobble(8.0);
        state.Update(0.02);
        Assert.True(Math.Abs(state.TiltAngle) > 0.5, "Tilt angle should wobble when triggered");
    }

    [Fact]
    public void Winking_animates_single_eye_differently()
    {
        var state = new BlobatarState();
        state.TriggerWink(leftEye: true);
        state.Update(0.065);

        Assert.True(state.LeftBlinkScaleY < 0.4, $"Left eye should be closed in left wink: {state.LeftBlinkScaleY}");
        Assert.Equal(1.0, state.RightBlinkScaleY);
    }

    [Fact]
    public void Celebration_tone_triggers_happy_expression()
    {
        var state = new BlobatarState();
        state.SetTone(BlobatarTone.Celebration);
        state.Update(0.1);

        Assert.True(state.HappyCurve > 0.4, $"Happy curve should be active on celebration: {state.HappyCurve}");
    }

    [Fact]
    public void Independent_eyes_converge_when_cursor_is_between_them()
    {
        var state = new BlobatarState();
        // Cursor placed at X=52.5, Y=45.0 (between the eyes: Left Eye is at 41.8, Right Eye is at 63.3)
        state.LookAtCursor(new Point(52.5, 45.0));

        for (var i = 0; i < 30; i++)
            state.Update(1 / 60.0);

        // Left eye should gaze right toward center (+X)
        Assert.True(state.LeftGazeX > 0.5, $"Left eye should look right: {state.LeftGazeX}");
        // Right eye should gaze left toward center (-X)
        Assert.True(state.RightGazeX < -0.5, $"Right eye should look left: {state.RightGazeX}");
    }

    [Fact]
    public void Gaze_is_confined_within_elliptical_bounds_at_all_angles()
    {
        var state = new BlobatarState();

        // Test extreme cursor positions in 16 directions around the screen
        for (var a = 0; a < 16; a++)
        {
            var angle = a * Math.PI / 8.0;
            var target = new Point(50.0 + Math.Cos(angle) * 5000.0, 52.5 + Math.Sin(angle) * 5000.0);
            state.LookAtCursor(target);

            for (var i = 0; i < 40; i++)
                state.Update(1 / 60.0);

            // Elliptical boundary constraint check for Left Eye (MaxX=4.8, MaxY=4.0)
            var leftNorm = Math.Pow(state.LeftGazeX / 4.8, 2) + Math.Pow(state.LeftGazeY / 4.0, 2);
            Assert.True(leftNorm <= 1.002, $"Left eye exceeded boundary at angle {angle}: norm={leftNorm}");

            // Elliptical boundary constraint check for Right Eye (MaxX=4.6, MaxY=3.8)
            var rightNorm = Math.Pow(state.RightGazeX / 4.6, 2) + Math.Pow(state.RightGazeY / 3.8, 2);
            Assert.True(rightNorm <= 1.002, $"Right eye exceeded boundary at angle {angle}: norm={rightNorm}");
        }
    }

    [Fact]
    public void Eye_spring_settles_and_goes_idle_when_cursor_stationary()
    {
        var state = new BlobatarState();
        state.LookAtCursor(new Point(90, 80));

        for (var i = 0; i < 70; i++)
            state.Update(1 / 60.0);

        // Eyes have settled and should report inactive when blinking is not running
        var active = state.Update(1 / 60.0, motionEnabled: false);
        Assert.False(active, "State should be idle when springs have settled");
    }

    [Fact]
    public void Near_field_deadzone_eases_singularity_at_eye_center()
    {
        var state = new BlobatarState();
        // Cursor placed directly at Left Eye center (41.8, 44.8)
        state.LookAtCursor(new Point(41.8, 44.8));

        for (var i = 0; i < 30; i++)
            state.Update(1 / 60.0);

        // Near-field deadzone eases gaze to 0
        Assert.True(Math.Abs(state.LeftGazeX) < 0.05, $"Left eye gaze should be eased to near 0: {state.LeftGazeX}");
        Assert.True(Math.Abs(state.LeftGazeY) < 0.25, $"Left eye gaze should be eased to near 0: {state.LeftGazeY}");
    }

    [AvaloniaFact]
    public void Donut_menu_shows_mascot_by_default()
    {
        var donut = new DonutMenu();
        Assert.True(donut.ShowCenterMascot);
    }

    [Fact]
    public void Left_gaze_expresses_natural_directional_attitude_and_3D_foreshortening()
    {
        var state = new BlobatarState();
        state.LookAt(new Point(-500, 0));
        for (var i = 0; i < 40; i++)
            state.Update(1 / 60.0);

        Assert.True(state.GazeX < -2.0, "Gaze should move left");
        Assert.True(state.LeftEyeTiltDeg < -3.0, $"Left eye should tilt counter-clockwise: {state.LeftEyeTiltDeg}");
        Assert.True(state.RightEyeTiltDeg < -3.0, $"Right eye should tilt counter-clockwise: {state.RightEyeTiltDeg}");
        Assert.True(state.RightForeshortenX < 0.95, $"Trailing right eye should foreshorten: {state.RightForeshortenX}");
        Assert.True(state.LeftForeshortenX >= 1.0, $"Leading left eye should retain full width: {state.LeftForeshortenX}");
    }

    [Fact]
    public void Right_gaze_expresses_natural_directional_attitude_and_3D_foreshortening()
    {
        var state = new BlobatarState();
        state.LookAt(new Point(500, 0));
        for (var i = 0; i < 40; i++)
            state.Update(1 / 60.0);

        Assert.True(state.GazeX > 2.0, "Gaze should move right");
        Assert.True(state.LeftEyeTiltDeg > 3.0, $"Left eye should tilt clockwise: {state.LeftEyeTiltDeg}");
        Assert.True(state.RightEyeTiltDeg > 3.0, $"Right eye should tilt clockwise: {state.RightEyeTiltDeg}");
        Assert.True(state.LeftForeshortenX < 0.95, $"Trailing left eye should foreshorten: {state.LeftForeshortenX}");
        Assert.True(state.RightForeshortenX >= 1.0, $"Leading right eye should retain full width: {state.RightForeshortenX}");
    }

    [Fact]
    public void Diagonal_gaze_tilts_smoothly_without_exceeding_natural_bounds()
    {
        var state = new BlobatarState();
        // Upper-right diagonal target
        state.LookAt(new Point(400, -300));
        for (var i = 0; i < 40; i++)
            state.Update(1 / 60.0);

        Assert.True(state.GazeX > 1.5, "Gaze X should be positive");
        Assert.True(state.GazeY < -1.0, "Gaze Y should be negative (upwards)");
        Assert.InRange(Math.Abs(state.LeftEyeTiltDeg), 0.5, 12.0);
        Assert.InRange(Math.Abs(state.RightEyeTiltDeg), 0.5, 12.0);
    }

    [AvaloniaFact]
    public void Renderer_renders_directional_gaze_at_extreme_angles_without_error()
    {
        var state = new BlobatarState();
        state.LookAt(new Point(-1000, 800)); // Extreme bottom-left
        for (var i = 0; i < 40; i++)
            state.Update(1 / 60.0);

        var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(100, 100));
        using var context = bitmap.CreateDrawingContext();
        BlobatarRenderer.Render(context, new Rect(0, 0, 100, 100), state);
    }

    [AvaloniaFact]
    public void Renderer_draws_the_companion_swallow_mouth_without_error()
    {
        var state = new BlobatarState();
        var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(100, 100));
        using var context = bitmap.CreateDrawingContext();
        BlobatarRenderer.Render(context, new Rect(0, 0, 100, 100), state, mouthOpen: 1f);
    }
}
