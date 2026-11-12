using Avalonia;
using Filee.App.Services;
using Filee.Core.Platform;
using Filee.Core.Settings;
using System.Text.Json;
using Xunit;

namespace Filee.App.Tests;

public sealed class BlobAppAwarenessTests
{
    [Theory]
    [InlineData("claude", "claude")]
    [InlineData("ChatGPT", "chatgpt")]
    [InlineData("antigravity.exe", "antigravity")]
    public void Matches_desktop_apps_by_process_identity(string process, string expected) =>
        Assert.Equal(expected, BlobAppAwareness.Match(new ForegroundAppContext(process)));

    [Theory]
    [InlineData("CabinetWClass", true)]
    [InlineData("ExploreWClass", true)]
    [InlineData("Shell_TrayWnd", false)]
    [InlineData("NotifyIconOverflowWindow", false)]
    public void Explorer_quips_only_match_file_windows_not_shell_surfaces(string windowClass, bool expected)
    {
        var context = new ForegroundAppContext("explorer", ProcessId: 22, WindowHandle: 110, WindowClass: windowClass);
        Assert.Equal(expected ? "file-explorer" : null, BlobAppAwareness.Match(context));
        Assert.Equal(expected, BlobAppAwareness.IsExplorerOrDesktop(context));
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    public void Desktop_shell_is_selection_aware_but_does_not_get_an_app_open_quip(string windowClass)
    {
        var context = new ForegroundAppContext("explorer", ProcessId: 22, WindowHandle: 110, WindowClass: windowClass);
        Assert.True(BlobAppAwareness.IsExplorerOrDesktop(context));
        Assert.Null(BlobAppAwareness.Match(context));
    }

    [Theory]
    [InlineData("A conversation - ChatGPT", "chatgpt")]
    [InlineData("Claude - Google Chrome", "claude")]
    [InlineData("LinkedIn: Feed - Google Chrome", "linkedin")]
    [InlineData("Random page - Google Chrome", "chrome")]
    [InlineData("Claude and ChatGPT - Google Chrome", null)]
    public void Chrome_title_match_takes_precedence_over_generic_chrome(string title, string? expected) =>
        Assert.Equal(expected, BlobAppAwareness.Match(new ForegroundAppContext("chrome", title)));

    [Fact]
    public void Companion_and_local_awareness_are_enabled_by_default_with_app_rules_ready()
    {
        var companion = new BlobCompanionSettings();
        Assert.True(companion.IsVisible);
        var settings = new BlobAppAwarenessSettings();
        Assert.True(settings.Enabled);
        Assert.Contains("claude", settings.EnabledApps);
        Assert.Contains("linkedin", settings.EnabledApps);
        Assert.Equal(10, settings.EnabledApps.Count);
    }

    [Theory]
    [InlineData("netflix", null, true)]
    [InlineData("chrome", "Netflix Watch - Google Chrome", true)]
    [InlineData("chrome", "YouTube - Google Chrome", true)]
    [InlineData("chrome", "HBO Max - Google Chrome", true)]
    [InlineData("chrome", "Maximum tabs - Google Chrome", false)]
    [InlineData("chrome", "ChatGPT - Google Chrome", false)]
    [InlineData("spotify", null, false)]
    public void Streaming_detection_requires_a_recognized_video_service(string process, string? title, bool expected) =>
        Assert.Equal(expected, BlobAppAwareness.IsStreamingService(new ForegroundAppContext(process, title)));

    [Theory]
    [InlineData(false, "netflix", false)]
    [InlineData(true, "netflix", true)]
    [InlineData(true, "powerpnt", false)]
    public void Automatic_hide_requires_both_fullscreen_and_a_streaming_service(bool fullscreen,
        string process, bool expected) =>
        Assert.Equal(expected, BlobAppAwareness.ShouldHideForStreaming(fullscreen,
            new ForegroundAppContext(process)));

    [Fact]
    public void Each_app_has_five_variants_and_picker_avoids_immediate_repeats_per_key()
    {
        Assert.All(BlobAppAwareness.Reactions, reaction => Assert.Equal(5, reaction.Quips.Length));
        Assert.Equal(5, BlobAppAwareness.SingleFileQuips.Count);
        Assert.Equal(5, BlobAppAwareness.MultipleFileQuips.Count);
        Assert.Equal(TimeSpan.FromSeconds(6), BlobAppAwareness.AppReactionDuration);

        var picker = new BlobQuipPicker(new Random(42));
        var first = picker.Next("claude", BlobAppAwareness.ForKey("claude")!.Quips);
        var second = picker.Next("claude", BlobAppAwareness.ForKey("claude")!.Quips);
        var otherApp = picker.Next("chatgpt", BlobAppAwareness.ForKey("chatgpt")!.Quips);
        Assert.NotEqual(first, second);
        Assert.Contains(otherApp, BlobAppAwareness.ForKey("chatgpt")!.Quips);
    }

    [Fact]
    public void File_drag_gaze_is_bounded_for_edges_and_empty_window_margins()
    {
        Assert.Equal(new Point(14, 88), BlobAppAwareness.ClampDragGaze(new Point(-12, 120)));
        Assert.Equal(new Point(86, 16), BlobAppAwareness.ClampDragGaze(new Point(120, -12)));
        Assert.Equal(new Point(50, 50), BlobAppAwareness.ClampDragGaze(new Point(50, 50)));
    }

    [Fact]
    public void Foreground_tracker_deduplicates_per_process_or_explorer_window_and_allows_new_processes()
    {
        var tracker = new ForegroundActivationTracker();
        var initial = new ForegroundAppContext("explorer", ProcessId: 11, WindowHandle: 101,
            WindowClass: "CabinetWClass", ProcessStartTimeUtcTicks: 100);
        tracker.EstablishBaseline(initial);

        Assert.False(tracker.Enter(initial, out var duplicate));
        Assert.Null(duplicate);

        var relaunched = initial with { ProcessId = 12, WindowHandle = 202, ProcessStartTimeUtcTicks = 200 };
        Assert.True(tracker.Enter(relaunched, out var freshLaunch));
        Assert.Equal("file-explorer", freshLaunch);

        var newWindow = relaunched with { WindowHandle = 303 };
        Assert.True(tracker.Enter(newWindow, out var windowEntry));
        Assert.Equal("file-explorer", windowEntry);

        Assert.True(tracker.Enter(initial, out var initialWindowAgain));
        Assert.Null(initialWindowAgain);

        var chatgpt = new ForegroundAppContext("chatgpt", ProcessId: 30, WindowHandle: 300,
            ProcessStartTimeUtcTicks: 300);
        Assert.True(tracker.Enter(chatgpt, out var nativeEntry));
        Assert.Equal("chatgpt", nativeEntry);
        var otherWindow = chatgpt with { WindowHandle = 301 };
        Assert.False(tracker.Enter(otherWindow, out var sameProcessEntry));
        Assert.Null(sameProcessEntry);
        var relaunchedChatGpt = chatgpt with { ProcessStartTimeUtcTicks = 301 };
        Assert.True(tracker.Enter(relaunchedChatGpt, out var newProcessEntry));
        Assert.Equal("chatgpt", newProcessEntry);

        var chrome = new ForegroundAppContext("chrome", "Claude - Google Chrome", 42, 400,
            "Chrome_WidgetWin_1", ProcessStartTimeUtcTicks: 420);
        Assert.True(tracker.Enter(chrome, out var siteEntry));
        Assert.Equal("claude", siteEntry);
        Assert.False(tracker.Enter(chrome, out _));
        Assert.False(tracker.Enter(chrome with { WindowHandle = 401 }, out var sameSiteOtherWindow));
        Assert.Null(sameSiteOtherWindow);
        Assert.True(tracker.Enter(chrome with { WindowTitle = "ChatGPT - Google Chrome" }, out var otherSite));
        Assert.Equal("chatgpt", otherSite);
    }

    [Fact]
    public void File_selection_quips_wait_for_a_stable_changed_selection_and_reset_on_clear()
    {
        var debouncer = new FileSelectionDebouncer(TimeSpan.FromMilliseconds(350));
        var start = DateTime.UnixEpoch;
        debouncer.EstablishBaseline(["C:\\temp\\first.txt"], start);

        Assert.Null(debouncer.Update(["C:\\temp\\first.txt"], start.AddSeconds(1)));
        Assert.Null(debouncer.Update(["C:\\temp\\second.txt"], start.AddSeconds(2)));
        Assert.Null(debouncer.Update(["C:\\temp\\second.txt"], start.AddMilliseconds(2349)));
        Assert.Equal(1, debouncer.Update(["C:\\temp\\second.txt"], start.AddMilliseconds(2350)));

        Assert.Null(debouncer.Update(["C:\\temp\\second.txt", "C:\\temp\\third.txt"], start.AddMilliseconds(2400)));
        Assert.Equal(2, debouncer.Update(["C:\\temp\\third.txt", "C:\\temp\\second.txt"], start.AddMilliseconds(2750)));

        Assert.Null(debouncer.Update([], start.AddMilliseconds(2800)));
        Assert.Null(debouncer.Update([], start.AddMilliseconds(3150)));
        Assert.Null(debouncer.Update([], start.AddMilliseconds(3200)));
        Assert.Null(debouncer.Update(["C:\\temp\\fourth.txt"], start.AddMilliseconds(3300)));
        Assert.Equal(1, debouncer.Update(["C:\\temp\\fourth.txt"], start.AddMilliseconds(3650)));
        Assert.Equal("Ooh, a file! At last—something tasty.", BlobAppAwareness.SingleFileQuips[0]);
        Assert.Equal("A whole bunch? I’m having a feast!", BlobAppAwareness.MultipleFileQuips[0]);
    }

    [Fact]
    public void Focused_input_gaze_expires_after_three_seconds_and_null_bounds_fall_back_to_cursor()
    {
        Assert.True(BlobFocusGaze.HasRecentInput(TimeSpan.FromSeconds(3)));
        Assert.False(BlobFocusGaze.HasRecentInput(TimeSpan.FromMilliseconds(3001)));
        Assert.False(BlobFocusGaze.HasRecentInput(null));

        var target = BlobFocusGaze.ToViewBoxTarget(new PlatformScreenBounds(300, 200, 100, 40),
            new PixelPoint(200, 100), 2, new Rect(0, 0, 100, 100));
        Assert.NotNull(target);
        Assert.Null(BlobFocusGaze.ToViewBoxTarget(null, new PixelPoint(200, 100), 2, new Rect(0, 0, 100, 100)));
    }

    [Theory]
    [InlineData(false, false, false, false, false, false, false, false)]
    [InlineData(true, true, false, false, false, false, false, false)]
    [InlineData(true, false, true, false, false, false, false, false)]
    [InlineData(true, false, false, true, false, false, false, false)]
    [InlineData(true, false, false, false, true, false, false, false)]
    [InlineData(true, false, false, false, false, false, false, true)]
    public void Awareness_is_suppressed_when_companion_is_busy(bool visible, bool paused, bool gesture,
        bool dragging, bool dropping, bool moving, bool pressed, bool wheelVisible) =>
        Assert.False(BlobAppAwareness.CanObserve(visible, paused, gesture, dragging, dropping, moving, pressed, wheelVisible));

    [Fact]
    public void Awareness_is_allowed_only_when_companion_is_idle_and_visible() =>
        Assert.True(BlobAppAwareness.CanObserve(true, false, false, false, false, false, false, false));

    [Fact]
    public void Awareness_master_and_app_choices_persist_in_settings_json()
    {
        var settings = new AppSettings();
        settings.BlobCompanion.AppAwareness.EnabledApps.Remove("claude");
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.True(restored.BlobCompanion.AppAwareness.Enabled);
        Assert.DoesNotContain("claude", restored.BlobCompanion.AppAwareness.EnabledApps);
        Assert.Contains("chatgpt", restored.BlobCompanion.AppAwareness.EnabledApps);
    }
}
