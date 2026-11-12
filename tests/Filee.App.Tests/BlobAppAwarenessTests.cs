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
    [InlineData("explorer", "file-explorer")]
    [InlineData("antigravity.exe", "antigravity")]
    public void Matches_desktop_apps_by_process_identity(string process, string expected) =>
        Assert.Equal(expected, BlobAppAwareness.Match(new ForegroundAppContext(process)));

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
    public void Chrome_title_match_takes_precedence_over_generic_chrome(string title, string expected) =>
        Assert.Equal(expected, BlobAppAwareness.Match(new ForegroundAppContext("chrome", title)));

    [Fact]
    public void Awareness_is_opt_in_with_app_rules_ready_when_enabled()
    {
        var settings = new BlobAppAwarenessSettings();
        Assert.False(settings.Enabled);
        Assert.Contains("claude", settings.EnabledApps);
        Assert.Contains("linkedin", settings.EnabledApps);
        Assert.Equal(10, settings.EnabledApps.Count);
    }

    [Fact]
    public void Each_app_has_five_variants_and_picker_avoids_immediate_repeats_per_key()
    {
        Assert.All(BlobAppAwareness.Reactions, reaction => Assert.Equal(5, reaction.Quips.Length));
        Assert.Equal(5, BlobAppAwareness.SingleFileQuips.Count);
        Assert.Equal(5, BlobAppAwareness.MultipleFileQuips.Count);

        var picker = new BlobQuipPicker(new Random(42));
        var first = picker.Next("claude", BlobAppAwareness.ForKey("claude")!.Quips);
        var second = picker.Next("claude", BlobAppAwareness.ForKey("claude")!.Quips);
        var otherApp = picker.Next("chatgpt", BlobAppAwareness.ForKey("chatgpt")!.Quips);
        Assert.NotEqual(first, second);
        Assert.Contains(otherApp, BlobAppAwareness.ForKey("chatgpt")!.Quips);
    }

    [Fact]
    public void Foreground_tracker_detects_new_processes_windows_and_site_entries_but_debounces_duplicates()
    {
        var tracker = new ForegroundActivationTracker();
        var initial = new ForegroundAppContext("explorer", ProcessId: 11, WindowHandle: 101, WindowClass: "CabinetWClass");
        tracker.EstablishBaseline(initial);

        Assert.False(tracker.Enter(initial, out var duplicate));
        Assert.Null(duplicate);

        var relaunched = initial with { ProcessId = 12, WindowHandle = 202 };
        Assert.True(tracker.Enter(relaunched, out var freshLaunch));
        Assert.Equal("file-explorer", freshLaunch);

        var newWindow = relaunched with { WindowHandle = 303 };
        Assert.True(tracker.Enter(newWindow, out var windowEntry));
        Assert.Equal("file-explorer", windowEntry);

        var chrome = new ForegroundAppContext("chrome", "Claude - Google Chrome", 42, 400, "Chrome_WidgetWin_1");
        Assert.True(tracker.Enter(chrome, out var siteEntry));
        Assert.Equal("claude", siteEntry);
        Assert.False(tracker.Enter(chrome, out _));
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
        settings.BlobCompanion.AppAwareness.Enabled = true;
        settings.BlobCompanion.AppAwareness.EnabledApps.Remove("claude");
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.True(restored.BlobCompanion.AppAwareness.Enabled);
        Assert.DoesNotContain("claude", restored.BlobCompanion.AppAwareness.EnabledApps);
        Assert.Contains("chatgpt", restored.BlobCompanion.AppAwareness.EnabledApps);
    }
}
