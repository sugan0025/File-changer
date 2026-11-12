using Filee.Core.Platform;

namespace Filee.App.Services;

/// <summary>Static, local-only app labels and reactions; no window title is retained by this type.</summary>
internal static class BlobAppAwareness
{
    internal sealed record Reaction(string Key, string Label, string[] Quips, string[] ProcessNames, string[] BrowserKeywords);

    public static IReadOnlyList<Reaction> Reactions { get; } =
    [
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

    /// <summary>Matches only the foreground process and, for Chrome, a transient title keyword.</summary>
    public static string? Match(ForegroundAppContext? context)
    {
        if (context?.ProcessName is not { Length: > 0 } processName || context.IsDesktop)
            return null;

        var process = Path.GetFileNameWithoutExtension(processName).ToLowerInvariant();
        if (process == "chrome")
        {
            var title = context.WindowTitle;
            if (!string.IsNullOrEmpty(title))
            {
                var site = Reactions.FirstOrDefault(reaction => reaction.BrowserKeywords.Any(keyword =>
                    title.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
                if (site is not null)
                    return site.Key;
            }

            return ChromeReaction.Key;
        }

        return Reactions.FirstOrDefault(reaction => reaction.ProcessNames.Contains(process, StringComparer.OrdinalIgnoreCase))?.Key;
    }

    public static Reaction? ForKey(string? key) => key is null
        ? null
        : Reactions.FirstOrDefault(reaction => reaction.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static bool IsExplorerOrDesktop(ForegroundAppContext? context) => context is not null &&
        (context.IsDesktop || string.Equals(Path.GetFileNameWithoutExtension(context.ProcessName), "explorer", StringComparison.OrdinalIgnoreCase));

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

internal readonly record struct ForegroundActivation(string? AppKey, int ProcessId, long WindowHandle)
{
    public static ForegroundActivation From(ForegroundAppContext? context) =>
        new(BlobAppAwareness.Match(context), context?.ProcessId ?? 0, context?.WindowHandle ?? 0);
}

internal sealed class ForegroundActivationTracker
{
    private ForegroundActivation? _current;
    public bool HasBaseline => _current is not null;

    public void EstablishBaseline(ForegroundAppContext? context) => _current = ForegroundActivation.From(context);

    public bool Enter(ForegroundAppContext? context, out string? appKey)
    {
        var next = ForegroundActivation.From(context);
        var changed = _current is null || _current.Value != next;
        _current = next;
        appKey = changed ? next.AppKey : null;
        return changed;
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
