using Avalonia;
using Filee.Core.Platform;

namespace Filee.App.Services;

/// <summary>Static, local-only app labels and reactions; no window title is retained by this type.</summary>
internal static class BlobAppAwareness
{
    public static readonly TimeSpan AppReactionDuration = TimeSpan.FromSeconds(6);
    internal sealed record Reaction(string Key, string Label, string[] Quips, string[] ProcessNames, string[] BrowserKeywords);

    public static IReadOnlyList<Reaction> Reactions { get; } =
    [
        new("hoot", "HOOT",
        [
            "HOOT is open. I was just keeping your files company.",
            "Back at HOOT? I saved you a tiny spot at the desk.",
            "The boss is here. I’ll look professionally adorable.",
            "HOOT is ready. I brought my best troubleshooting face.",
            "Welcome back. I’ve been supervising the pixels."
        ], ["hoot"], []),
        new("claude", "Claude",
        [
            "Ooo, forgot how to code? Let Claude dust off those brain cells.",
            "Stuck on the code? Claude, fetch the tiny debugging hat.",
            "Another mystery bug? Let’s ask Claude before I chew the keyboard.",
            "Code’s acting mysterious again. Claude, bring your thinking face.",
            "One more prompt and this code might start paying rent."
        ], ["claude"], ["claude"]),
        new("chatgpt", "ChatGPT",
        [
            "Calling in another brain? I’ll supervise.",
            "Another prompt? Excellent. I’ll guard the snacks.",
            "Need a second opinion? I’m the first adorable one.",
            "Two brains, one tab. I hope somebody brought a plan.",
            "Ask ChatGPT; I’ll review the answer for snack potential."
        ], ["chatgpt", "codex"], ["chatgpt"]),
        new("antigravity", "Antigravity",
        [
            "Careful, your ideas are entering orbit.",
            "Launching another task? I packed a tiny helmet.",
            "Gravity called. It wants your tabs back.",
            "Mission control, that brainstorm is wobbling.",
            "One small click for you; one giant detour for the project."
        ], ["antigravity", "google.antigravity"], ["antigravity"]),
        new("chrome", "Chrome",
        [
            "Another tab? Excellent. We were getting lonely.",
            "How many tabs before Chrome gets a tiny hat?",
            "This tab has tabs. I respect the commitment.",
            "Lost in tab-land? I packed snacks.",
            "Your browser’s RAM just asked for a nap."
        ], ["chrome"], []),
        new("file-explorer", "File Explorer",
        [
            "File hunt! I’ll watch the mouse.",
            "Files everywhere. I’ll pretend this is organized.",
            "Folder quest! I’m your tiny guide.",
            "Those folders have folders. A plot twist.",
            "I saw that file first. Probably."
        ], ["explorer"], []),
        new("netflix", "Netflix",
        [
            "A very serious research break, I see.",
            "Research episode number four? I won’t tell.",
            "Plot twist: the Blob wants popcorn.",
            "Just one episode? Sure, I’ve heard that one.",
            "I’m reviewing the snacks, not the show."
        ], ["netflix"], ["netflix"]),
        new("spotify", "Spotify",
        [
            "Soundtrack acquired. Main-character mode: on.",
            "Now playing: ambitious desk vibes.",
            "Your playlist has better pacing than this project.",
            "Hit play; I’ll conduct with my little body.",
            "Soundtrack unlocked. Dramatic montage incoming."
        ], ["spotify"], ["spotify"]),
        new("whatsapp", "WhatsApp",
        [
            "A message! Should I pretend I didn’t see it?",
            "Reply later? I’ll guard the notification.",
            "Chat time. I’ll be your tiny audience.",
            "That typing bubble is making me nervous.",
            "Unread messages are just side quests."
        ], ["whatsapp", "whatsapp.root"], ["whatsapp"]),
        new("gemini", "Gemini",
        [
            "Two AI assistants? I’m still the cutest.",
            "Gemini’s here; I’ll supervise the constellation.",
            "Need another idea? I’ve got one: snacks.",
            "That’s a lot of intelligence in one window.",
            "Ask Gemini, then ask me. I have opinions."
        ], ["gemini"], ["gemini"]),
        new("linkedin", "LinkedIn",
        [
            "Professional mode: activated. I’ll try to look busy.",
            "Networking mode on. I’m practicing my tiny handshake.",
            "Another connection? I’m connected to snacks.",
            "Your profile looks busy; I’ll look busy too.",
            "Endorse me for excellent loafing."
        ], ["linkedin"], ["linkedin"]),
    ];

    public static IReadOnlyList<string> SingleFileQuips { get; } =
    [
        "Ooh, a file! At last—something tasty.",
        "One file? A dainty little snack!",
        "Fresh pickings! I call dibs on the imaginary crumbs.",
        "A file has arrived. My tiny appetite is ready.",
        "Careful, it might be crunchy."
    ];

    public static IReadOnlyList<string> MultipleFileQuips { get; } =
    [
        "A whole bunch? I’m having a feast!",
        "That’s a buffet! I’ll need my stretchy pants.",
        "More files than bites—challenge accepted.",
        "An entire file parade. I’m seated.",
        "Multiple snacks? My tiny mouth is impressed."
    ];

    private static readonly Reaction ChromeReaction = Reactions.First(reaction => reaction.Key == "chrome");

    /// <summary>Matches only the foreground process and, for recognized browsers, a transient title keyword with word boundary protection.</summary>
    public static string? Match(ForegroundAppContext? context)
    {
        if (context?.ProcessName is not { Length: > 0 } processName || context.IsDesktop)
            return null;

        var process = Path.GetFileNameWithoutExtension(processName).ToLowerInvariant();
        var isBrowser = Filee.Core.Web.WebBrowsers.IsBrowser(process);
        if (isBrowser)
        {
            var title = context.WindowTitle;
            if (!string.IsNullOrEmpty(title))
            {
                var sites = Reactions.Where(reaction => reaction.BrowserKeywords.Any(keyword =>
                    ContainsWholeTitleToken(title, keyword))).ToArray();
                // Ambiguous titles fail closed so one service never borrows another service's quip pool.
                if (sites.Length > 1)
                    return null;
                if (sites.Length == 1)
                    return sites[0].Key;
            }

            return process == "chrome" ? ChromeReaction.Key : null;
        }

        if (process == "explorer")
            return IsExplorerFolderWindow(context) ? "file-explorer" : null;

        if (string.Equals(process, "hoot", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(process, "filee", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(process, "filee.app", StringComparison.OrdinalIgnoreCase))
            return "hoot";

        return Reactions.FirstOrDefault(reaction => reaction.ProcessNames.Contains(process, StringComparer.OrdinalIgnoreCase))?.Key;
    }

    public static Reaction? ForKey(string? key) => key is null
        ? null
        : Reactions.FirstOrDefault(reaction => reaction.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static bool IsExplorerOrDesktop(ForegroundAppContext? context) => context is not null &&
        (context.IsDesktop || IsExplorerFolderWindow(context));

    public static bool IsExplorerFolderWindow(ForegroundAppContext? context) => context is not null
        && string.Equals(Path.GetFileNameWithoutExtension(context.ProcessName), "explorer", StringComparison.OrdinalIgnoreCase)
        && context.WindowClass is "CabinetWClass" or "ExploreWClass";

    public static bool IsShellTransition(ForegroundAppContext? context)
    {
        if (context is null) return true;
        var process = Path.GetFileNameWithoutExtension(context.ProcessName);
        var windowClass = context.WindowClass;

        if (string.Equals(process, "explorer", StringComparison.OrdinalIgnoreCase))
        {
            return windowClass is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
                or "NotifyIconOverflowWindow" or "TaskSwitcherWnd" or "MultitaskingViewFrame";
        }

        if (process is not null && (
            process.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("SearchApp", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("LockApp", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("LogonUI", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return windowClass == "#32768";
    }

    public static bool IsStreamingService(ForegroundAppContext? context)
    {
        if (context?.ProcessName is not { Length: > 0 } processName)
            return false;
        var process = Path.GetFileNameWithoutExtension(processName);
        if (process.Equals("netflix", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!process.Equals("chrome", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(context.WindowTitle))
            return false;
        string[] streamingKeywords = ["netflix", "youtube", "prime video", "disney+", "hulu", "max", "hotstar"];
        return streamingKeywords.Any(keyword => keyword == "max"
            ? ContainsWholeTitleToken(context.WindowTitle, keyword)
            : context.WindowTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsWholeTitleToken(string title, string token)
    {
        var offset = 0;
        while ((offset = title.IndexOf(token, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var beforeIsWord = offset > 0 && char.IsLetterOrDigit(title[offset - 1]);
            var after = offset + token.Length;
            var afterIsWord = after < title.Length && char.IsLetterOrDigit(title[after]);
            if (!beforeIsWord && !afterIsWord)
                return true;
            offset = after;
        }

        return false;
    }

    public static bool ShouldHideForStreaming(bool fullscreenOrPresentation, ForegroundAppContext? context) =>
        fullscreenOrPresentation && IsStreamingService(context);

    public static Point ClampDragGaze(Point target) => new(
        Math.Clamp(target.X, 14, 86), Math.Clamp(target.Y, 16, 88));

    public static bool CanObserve(bool visible, bool paused, bool gestureActive, bool dragActive,
        bool dropPending, bool moving, bool pressed, bool wheelVisible) =>
        visible && !paused && !gestureActive && !dragActive && !dropPending && !moving && !pressed && !wheelVisible;
}

internal sealed class BlobQuipPicker(Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;
    private readonly Dictionary<string, string> _lastByKey = new(StringComparer.OrdinalIgnoreCase);

    public string? Next(string key, IReadOnlyList<string> quips)
    {
        if (quips.Count == 0) return null;
        var last = _lastByKey.GetValueOrDefault(key);
        var candidates = quips.Where(quip => !string.Equals(quip, last, StringComparison.Ordinal)).ToArray();
        var selected = candidates[_random.Next(candidates.Length)];
        _lastByKey[key] = selected;
        return selected;
    }

    public void Clear() => _lastByKey.Clear();
}

internal readonly record struct ForegroundActivation(string? AppKey, int ProcessId, long ProcessStartTimeUtcTicks, long WindowHandle)
{
    public static ForegroundActivation From(ForegroundAppContext? context)
    {
        var appKey = BlobAppAwareness.Match(context);
        if (appKey is null)
            return new(null, 0, 0, 0);
        return new(appKey, context?.ProcessId ?? 0, context?.ProcessStartTimeUtcTicks ?? 0,
            appKey == "file-explorer" ? context?.WindowHandle ?? 0 : 0);
    }
}

internal sealed class ForegroundActivationTracker(TimeSpan? perAppCooldown = null, Func<DateTime>? clock = null)
{
    private static readonly TimeSpan DefaultPerAppCooldown = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _perAppCooldown = perAppCooldown ?? DefaultPerAppCooldown;
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private ForegroundActivation? _current;
    private readonly Dictionary<ForegroundActivation, DateTime> _reacted = [];
    public bool HasBaseline => _current is not null;

    public void EstablishBaseline(ForegroundAppContext? context)
    {
        _current = ForegroundActivation.From(context);
        // HOOT's main window is commonly foreground when the companion starts. Leave it unbased so
        // the first awareness tick can show the requested one-time HOOT welcome quip.
        if (_current.Value.AppKey == "hoot")
        {
            _current = null;
            return;
        }
        if (_current.Value.AppKey is not null)
            _reacted[_current.Value] = _clock();
    }

    public bool Enter(ForegroundAppContext? context, out string? appKey)
    {
        var next = ForegroundActivation.From(context);
        var changed = _current is null || _current.Value != next;
        _current = next;

        if (!changed || next.AppKey is null)
        {
            appKey = null;
            return changed;
        }

        var now = _clock();
        if (_reacted.TryGetValue(next, out var lastTime) && now - lastTime < _perAppCooldown)
        {
            appKey = null;
            return true;
        }

        _reacted[next] = now;
        appKey = next.AppKey;
        return true;
    }

    public void RetryUndelivered(ForegroundAppContext? context)
    {
        _reacted.Remove(ForegroundActivation.From(context));
        _current = null;
    }

    public void Reset() => _current = null;
}

internal sealed class FileSelectionDebouncer(TimeSpan settleDelay)
{
    private string[]? _stable;
    private string[]? _candidate;
    private DateTime _candidateSince;

    public void EstablishBaseline(IEnumerable<string> paths, DateTime now)
    {
        _stable = Normalize(paths);
        _candidate = _stable;
        _candidateSince = now;
    }

    public int? Update(IEnumerable<string> paths, DateTime now)
    {
        var snapshot = Normalize(paths);
        if (_candidate is null || !Same(_candidate, snapshot))
        {
            _candidate = snapshot;
            _candidateSince = now;
            return null;
        }

        if (_stable is not null && Same(_stable, snapshot)) return null;
        if (now - _candidateSince < settleDelay) return null;

        _stable = snapshot;
        return snapshot.Length == 0 ? null : snapshot.Length;
    }

    public void Reset()
    {
        _stable = null;
        _candidate = null;
        _candidateSince = default;
    }

    private static string[] Normalize(IEnumerable<string> paths) => paths
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool Same(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count == right.Count && left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
}
