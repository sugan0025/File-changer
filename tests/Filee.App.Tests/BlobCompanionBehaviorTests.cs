using Filee.App.Services;
using Filee.App.Controls;
using Filee.Core.Settings;
using Xunit;

namespace Filee.App.Tests;

public sealed class BlobCompanionBehaviorTests
{
    [Fact]
    public void Optional_platform_behaviors_default_off()
    {
        var settings = new BlobCompanionSettings();
        Assert.False(settings.CinemaDimEnabled);
        Assert.False(settings.StretchReminderEnabled);

        var appSettings = new AppSettings();
        appSettings.BlobCompanion.CinemaDimEnabled = true;
        appSettings.BlobCompanion.StretchReminderEnabled = true;
        var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(appSettings))!;
        Assert.True(restored.BlobCompanion.CinemaDimEnabled);
        Assert.True(restored.BlobCompanion.StretchReminderEnabled);
    }

    [Theory]
    [InlineData("spotify", "Sway")]
    [InlineData("chrome", "Scan")]
    [InlineData("file-explorer", "Scan")]
    [InlineData("claude", "Think")]
    [InlineData("antigravity", "Orbit")]
    [InlineData("linkedin", "Proud")]
    [InlineData("netflix", "Settle")]
    [InlineData("whatsapp", "DoubleBlink")]
    [InlineData("unknown", "None")]
    public void Each_supported_app_quip_maps_to_a_real_blob_body_motion(string app, string expected) =>
        Assert.Equal(expected, BlobCompanionBehavior.MotionForApp(app).ToString());

    [Fact]
    public void Moment_priority_allows_higher_priority_work_to_interrupt_lower_priority_work()
    {
        Assert.True(BlobCompanionBehavior.CanInterrupt(BlobCompanionMoment.Idle, BlobCompanionMoment.AppReaction));
        Assert.True(BlobCompanionBehavior.CanInterrupt(BlobCompanionMoment.AppReaction, BlobCompanionMoment.Conversion));
        Assert.True(BlobCompanionBehavior.CanInterrupt(BlobCompanionMoment.Conversion, BlobCompanionMoment.FileDrop));
        Assert.False(BlobCompanionBehavior.CanInterrupt(BlobCompanionMoment.FileDrop, BlobCompanionMoment.AppReaction));
        Assert.False(BlobCompanionBehavior.CanInterrupt(BlobCompanionMoment.Conversion, BlobCompanionMoment.UserReaction));
    }

    [Theory]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, false)]
    [InlineData(false, 0, false)]
    public void Only_valid_unpaused_file_drags_wake_the_companion(bool paused, int files, bool expected) =>
        Assert.Equal(expected, BlobCompanionBehavior.CanWakeForFiles(paused, files));

    [Theory]
    [InlineData(true, false, false, false, false, true)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, false, true, false, false, false)]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(false, false, false, false, false, false)]
    public void Hold_to_hug_requires_a_stationary_unmodified_unpaused_press(bool held, bool moved,
        bool controlHeld, bool paused, bool gestureActive, bool expected) =>
        Assert.Equal(expected, BlobCompanionBehavior.HugCompleted(held, moved, controlHeld, paused, gestureActive));

    [Fact]
    public void Nap_expires_only_at_its_deadline()
    {
        var deadline = DateTime.UnixEpoch.AddMinutes(30);
        Assert.False(BlobCompanionBehavior.NapExpired(deadline, deadline.AddTicks(-1)));
        Assert.True(BlobCompanionBehavior.NapExpired(deadline, deadline));
        Assert.False(BlobCompanionBehavior.NapExpired(null, deadline));
    }

    [Fact]
    public void Wheel_reemergence_has_finite_endpoints_and_a_small_overshoot()
    {
        Assert.Equal(.14, BlobCompanionBehavior.ReemergenceAt(0).Scale, 5);
        Assert.Equal(0, BlobCompanionBehavior.ReemergenceAt(0).Opacity, 5);
        Assert.InRange(BlobCompanionBehavior.ReemergenceAt(.78).Scale, 1.15, 1.17);
        Assert.Equal(1, BlobCompanionBehavior.ReemergenceAt(1).Scale, 5);
        Assert.Equal(1, BlobCompanionBehavior.ReemergenceAt(1).Opacity, 5);
    }

    [Fact]
    public void Perch_chooses_only_a_body_lean_and_never_changes_window_position()
    {
        Assert.Equal(-6, BlobCompanionBehavior.PerchLean(100, 100, 100, 1200, 1));
        Assert.Equal(6, BlobCompanionBehavior.PerchLean(1186, 100, 100, 1200, 1));
        Assert.Equal(0, BlobCompanionBehavior.PerchLean(500, 100, 100, 1200, 1));
    }

    [Fact]
    public void Fullness_is_transient_caps_at_five_and_decays_over_time()
    {
        Assert.Equal(1, BlobCompanionBehavior.Fullness(10, TimeSpan.Zero));
        Assert.Equal(.8f, BlobCompanionBehavior.Fullness(5, TimeSpan.FromMinutes(20)), 3);
        Assert.Equal(0, BlobCompanionBehavior.Fullness(5, TimeSpan.FromMinutes(100)));
        Assert.Equal(0, BlobCompanionBehavior.Fullness(0, TimeSpan.Zero));
    }

    [Fact]
    public void Yank_requires_four_direction_reversals_inside_one_second()
    {
        var detector = new BlobDragYankDetector();
        var start = DateTime.UnixEpoch;
        detector.Record(10, 0, start);
        Assert.False(detector.IsYank(start));
        detector.Record(-10, 0, start.AddMilliseconds(100));
        detector.Record(10, 0, start.AddMilliseconds(200));
        detector.Record(-10, 0, start.AddMilliseconds(300));
        detector.Record(10, 0, start.AddMilliseconds(400));
        Assert.True(detector.IsYank(start.AddMilliseconds(400)));
        Assert.False(detector.IsYank(start.AddSeconds(2)));
        detector.Reset();
        Assert.False(detector.IsYank(start.AddMilliseconds(500)));
    }

    [Fact]
    public void Stretch_reminder_requires_an_hour_of_recent_activity_and_enforces_ninety_minute_cooldown()
    {
        var scheduler = new StretchReminderSchedule();
        var start = DateTime.UnixEpoch;
        Assert.False(scheduler.Update(TimeSpan.FromSeconds(2), start, true, true));
        Assert.False(scheduler.Update(TimeSpan.FromSeconds(1), start.AddMinutes(59), true, true));
        Assert.True(scheduler.Update(TimeSpan.FromSeconds(1), start.AddMinutes(60), true, true));
        Assert.False(scheduler.Update(TimeSpan.FromSeconds(1), start.AddMinutes(149), true, true));
        Assert.True(scheduler.Update(TimeSpan.FromSeconds(1), start.AddMinutes(150), true, true));
    }

    [Fact]
    public void Stretch_reminder_resets_after_long_idle_and_waits_until_it_can_display()
    {
        var scheduler = new StretchReminderSchedule();
        var start = DateTime.UnixEpoch;
        Assert.False(scheduler.Update(TimeSpan.FromSeconds(1), start, true, true));
        Assert.False(scheduler.Update(TimeSpan.FromSeconds(1), start.AddMinutes(61), true, false));
        Assert.True(scheduler.Update(TimeSpan.FromSeconds(1), start.AddMinutes(62), true, true));
        Assert.False(scheduler.Update(TimeSpan.FromMinutes(2), start.AddMinutes(63), true, true));
        Assert.False(scheduler.Update(TimeSpan.FromSeconds(1), start.AddMinutes(124), true, true));
    }

    [Fact]
    public void New_lean_fullness_and_eye_roll_inputs_affect_only_companion_physics()
    {
        var mainBlob = new BlobatarState();
        var companion = new BlobatarState { CompanionMode = true };
        companion.SetRestLean(6);
        companion.SetCompanionFullness(1);
        companion.TriggerCompanionEyeRoll(10, 1.4);

        for (var frame = 0; frame < 30; frame++)
        {
            mainBlob.Update(1d / 30);
            companion.Update(1d / 30);
        }

        Assert.Equal(0, mainBlob.Frame.LeanDeg, 3);
        Assert.True(companion.Frame.LeanDeg > 0);
        Assert.True(companion.Frame.Sy > mainBlob.Frame.Sy);
        Assert.NotEqual(0, companion.Frame.Left.Roll);
        Assert.Equal(0, mainBlob.Frame.Left.Roll, 3);
    }
}
