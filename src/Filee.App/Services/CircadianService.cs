using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Filee.Core.Settings;

namespace Filee.App.Services;

public sealed class CircadianService
{
    public sealed record CircadianFrame(
        Color ScrimTop,
        Color ScrimMid,
        Color ScrimBottom,
        Color CardTop,
        Color CardBottom,
        Color EdgeTop,
        Color EdgeBottom,
        Color InnerHighlight,
        Color Shadow,
        Color AmbientReflection,
        Color MasterCard,
        Color Subcard,
        Color ConsoleBar,
        Color TextPrimary,
        Color TextSecondary,
        double KeyLightAngle,
        double SpecularGain,
        bool IsDark
    );

    // Morning: Icy, high contrast, crisp cool specular rim (06:00 - 12:00)
    public static readonly CircadianFrame MorningFrame = new(
        ScrimTop: Color.Parse("#70F4F8FF"),
        ScrimMid: Color.Parse("#8CF4F8FF"),
        ScrimBottom: Color.Parse("#A0E0EAFC"),
        CardTop: Color.Parse("#9EFFFFFF"),
        CardBottom: Color.Parse("#6BEBF4FF"),
        EdgeTop: Color.Parse("#E6FFFFFF"),
        EdgeBottom: Color.Parse("#73B4CDEB"),
        InnerHighlight: Color.Parse("#F2FFFFFF"),
        Shadow: Color.Parse("#2E284678"),
        AmbientReflection: Color.Parse("#59BFE0FF"),
        MasterCard: Color.Parse("#E6FFFFFF"),
        Subcard: Color.Parse("#99FFFFFF"),
        ConsoleBar: Color.Parse("#CCFFFFFF"),
        TextPrimary: Color.Parse("#0E1726"),
        TextSecondary: Color.Parse("#4A5A72"),
        KeyLightAngle: 300.0 * Math.PI / 180.0, // 10 o'clock
        SpecularGain: 1.0,
        IsDark: false
    );

    // Afternoon: Warm champagne, softened golden reflections (12:00 - 18:00)
    public static readonly CircadianFrame AfternoonFrame = new(
        ScrimTop: Color.Parse("#50FFF1DC"),
        ScrimMid: Color.Parse("#66FFF1DC"),
        ScrimBottom: Color.Parse("#85FCE0BE"),
        CardTop: Color.Parse("#8CFFF4E2"),
        CardBottom: Color.Parse("#61F6E0BE"),
        EdgeTop: Color.Parse("#D9FFECCD"),
        EdgeBottom: Color.Parse("#66D6A05A"),
        InnerHighlight: Color.Parse("#F2FFD9A0"),
        Shadow: Color.Parse("#33784614"),
        AmbientReflection: Color.Parse("#4DFFD9A0"),
        MasterCard: Color.Parse("#EDFBF4E4"),
        Subcard: Color.Parse("#A6FFF6E6"),
        ConsoleBar: Color.Parse("#D9FFF1DC"),
        TextPrimary: Color.Parse("#2B1E10"),
        TextSecondary: Color.Parse("#6A5641"),
        KeyLightAngle: 60.0 * Math.PI / 180.0, // 2 o'clock low
        SpecularGain: 0.7,
        IsDark: false
    );

    // Night: Deep obsidian glass, luminous hairline edges (18:00 - 06:00)
    public static readonly CircadianFrame NightFrame = new(
        ScrimTop: Color.Parse("#4005070D"),
        ScrimMid: Color.Parse("#550A0C12"),
        ScrimBottom: Color.Parse("#80040508"),
        CardTop: Color.Parse("#66141826"),
        CardBottom: Color.Parse("#4D0A0D16"),
        EdgeTop: Color.Parse("#66B8C6FF"),
        EdgeBottom: Color.Parse("#1AFFFFFF"),
        InnerHighlight: Color.Parse("#38FFFFFF"),
        Shadow: Color.Parse("#59000000"),
        AmbientReflection: Color.Parse("#1F8E85F3"),
        MasterCard: Color.Parse("#35141926"),
        Subcard: Color.Parse("#22161A26"),
        ConsoleBar: Color.Parse("#2B1A2030"),
        TextPrimary: Color.Parse("#F2F5FF"),
        TextSecondary: Color.Parse("#AEB8D0"),
        KeyLightAngle: 0.0, // 12 o'clock
        SpecularGain: 1.25,
        IsDark: true
    );

    // Mutable brush instances held permanently in memory
    private readonly LinearGradientBrush _scrimBrush = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
    };
    private readonly LinearGradientBrush _cardBrush = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
    };
    private readonly LinearGradientBrush _cardHoverBrush = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
    };
    private readonly LinearGradientBrush _edgeBrush = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
    };
    private readonly LinearGradientBrush _edgeHoverBrush = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
    };

    private readonly SolidColorBrush _innerHighlightBrush = new();
    private readonly SolidColorBrush _ambientReflectionBrush = new();
    private readonly SolidColorBrush _masterCardBrush = new();
    private readonly SolidColorBrush _subcardBrush = new();
    private readonly SolidColorBrush _consoleBarBrush = new();
    private readonly SolidColorBrush _textPrimaryBrush = new();
    private readonly SolidColorBrush _textSecondaryBrush = new();

    private readonly DispatcherTimer _timer;
    private IResourceDictionary? _resources;
    private Application? _app;
    private CircadianSettings _settings = new();
    private CircadianFrame? _lastFrame;
    private double? _previewMinute;

    public CircadianPhase CurrentPhase { get; private set; } = CircadianPhase.Night;
    public double CurrentKeyLightAngle { get; private set; } = 0.0;
    public double CurrentSpecularGain { get; private set; } = 1.0;

    public event EventHandler? PhaseChanged;

    public CircadianService()
    {
        // 5s Background priority timer as recommended by Claude
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => Tick());

        // Initialize stops for gradient brushes
        _scrimBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
        _scrimBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.55));
        _scrimBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));

        _cardBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
        _cardBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));

        _cardHoverBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
        _cardHoverBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));

        _edgeBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
        _edgeBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.45));
        _edgeBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));

        _edgeHoverBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
        _edgeHoverBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.45));
        _edgeHoverBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
    }

    public void Attach(IResourceDictionary res, Application app)
    {
        _resources = res;
        _app = app;

        // Register mutable brushes directly at top-level resources
        res["PhaseScrimBrush"] = _scrimBrush;
        res["GlassCardBrush"] = _cardBrush;
        res["GlassCardHoverBrush"] = _cardHoverBrush;
        res["GlassEdgeBrush"] = _edgeBrush;
        res["GlassEdgeHoverBrush"] = _edgeHoverBrush;
        res["GlassInnerHighlightBrush"] = _innerHighlightBrush;
        res["AmbientReflectionBrush"] = _ambientReflectionBrush;
        res["GlassMasterCardBrush"] = _masterCardBrush;
        res["GlassSubcardBrush"] = _subcardBrush;
        res["GlassConsoleBarBrush"] = _consoleBarBrush;
        res["TextPrimaryBrush"] = _textPrimaryBrush;
        res["TextSecondaryBrush"] = _textSecondaryBrush;

        Tick(force: true);
    }

    public void Configure(CircadianSettings settings)
    {
        _settings = settings;

        if (_settings.Mode == CircadianMode.Off)
        {
            _timer.Stop();
            // Revert brushes to active theme variant defaults
            ApplyVariantDefaults();
        }
        else
        {
            Tick(force: true);
            if (!_timer.IsEnabled)
                _timer.Start();
        }
    }

    public void SetPreviewMinute(double? minute)
    {
        _previewMinute = minute;
        Tick(force: true);
    }

    public void OnWindowActivated()
    {
        if (_settings.Mode != CircadianMode.Off)
            Tick(force: true);
    }

    private void Tick(bool force = false)
    {
        if (_resources == null || (_settings.Mode == CircadianMode.Off && _previewMinute == null))
            return;

        var frame = ResolveCurrentFrame();

        if (!force && _lastFrame != null && !HasMeaningfulDifference(_lastFrame, frame))
            return;

        ApplyFrame(frame);
        _lastFrame = frame;
    }

    private CircadianFrame ResolveCurrentFrame()
    {
        if (_settings.Mode == CircadianMode.Pinned && _settings.Pinned.HasValue && _previewMinute == null)
        {
            // Auto-release pinned mode if PinnedUntil has passed
            if (_settings.PinnedUntil.HasValue && DateTimeOffset.UtcNow > _settings.PinnedUntil.Value)
            {
                _settings.Mode = CircadianMode.Clock;
                _settings.Pinned = null;
            }
            else
            {
                CurrentPhase = _settings.Pinned.Value;
                return _settings.Pinned.Value switch
                {
                    CircadianPhase.Morning => MorningFrame,
                    CircadianPhase.Afternoon => AfternoonFrame,
                    _ => NightFrame
                };
            }
        }

        DateTime now = DateTime.Now;
        double minuteOfDay = _previewMinute ?? (now.Hour * 60 + now.Minute + now.Second / 60.0);

        double morningMin = _settings.MorningStart.Hour * 60 + _settings.MorningStart.Minute;
        double afternoonMin = _settings.AfternoonStart.Hour * 60 + _settings.AfternoonStart.Minute;
        double nightMin = _settings.NightStart.Hour * 60 + _settings.NightStart.Minute;

        if (_settings.Mode == CircadianMode.Sun && _settings.Latitude.HasValue && _settings.Longitude.HasValue)
        {
            var sun = SunCalc.Calculate(now, _settings.Latitude.Value, _settings.Longitude.Value);
            morningMin = sun.Sunrise.Hour * 60 + sun.Sunrise.Minute;
            afternoonMin = sun.SolarNoon.Hour * 60 + sun.SolarNoon.Minute;
            nightMin = sun.Sunset.Hour * 60 + sun.Sunset.Minute;
        }

        double band = _settings.TransitionMinutes;

        // Transitions:
        // Night -> Morning centered at morningMin
        // Morning -> Afternoon centered at afternoonMin
        // Afternoon -> Night centered at nightMin
        (CircadianFrame fromFrame, CircadianFrame toFrame, double t, CircadianPhase phase) =
            CalculatePhaseBlend(minuteOfDay, [morningMin, afternoonMin, nightMin], band);

        CurrentPhase = phase;

        if (!Motion.Enabled)
        {
            // Reduced motion: instant snap at boundary without cross-fade
            return t >= 0.5 ? toFrame : fromFrame;
        }

        return LerpFrames(fromFrame, toFrame, t);
    }

    private static (CircadianFrame From, CircadianFrame To, double T, CircadianPhase Phase) CalculatePhaseBlend(
        double min, double[] starts, double band)
    {
        double morning = starts[0];
        double afternoon = starts[1];
        double night = starts[2];

        // 1. Check boundary transitions within transition band
        double dMorning = SignedMinuteDelta(min, morning);
        if (Math.Abs(dMorning) <= band / 2.0)
        {
            double t = Smoothstep((dMorning + band / 2.0) / band);
            return (NightFrame, MorningFrame, t, t >= 0.5 ? CircadianPhase.Morning : CircadianPhase.Night);
        }

        double dAfternoon = SignedMinuteDelta(min, afternoon);
        if (Math.Abs(dAfternoon) <= band / 2.0)
        {
            double t = Smoothstep((dAfternoon + band / 2.0) / band);
            return (MorningFrame, AfternoonFrame, t, t >= 0.5 ? CircadianPhase.Afternoon : CircadianPhase.Morning);
        }

        double dNight = SignedMinuteDelta(min, night);
        if (Math.Abs(dNight) <= band / 2.0)
        {
            double t = Smoothstep((dNight + band / 2.0) / band);
            return (AfternoonFrame, NightFrame, t, t >= 0.5 ? CircadianPhase.Night : CircadianPhase.Afternoon);
        }

        // 2. Stable plateaus between transition windows
        if (min >= morning && min < afternoon)
            return (MorningFrame, MorningFrame, 1.0, CircadianPhase.Morning);
        if (min >= afternoon && min < night)
            return (AfternoonFrame, AfternoonFrame, 1.0, CircadianPhase.Afternoon);

        return (NightFrame, NightFrame, 1.0, CircadianPhase.Night);
    }

    private static double SignedMinuteDelta(double min, double target)
    {
        double diff = (min - target + 720.0) % 1440.0;
        if (diff < 0) diff += 1440.0;
        return diff - 720.0;
    }

    private static double Smoothstep(double x)
    {
        double c = Math.Clamp(x, 0.0, 1.0);
        return c * c * (3.0 - 2.0 * c);
    }

    private static CircadianFrame LerpFrames(CircadianFrame a, CircadianFrame b, double t)
    {
        return new CircadianFrame(
            ScrimTop: OkLab.Lerp(a.ScrimTop, b.ScrimTop, t),
            ScrimMid: OkLab.Lerp(a.ScrimMid, b.ScrimMid, t),
            ScrimBottom: OkLab.Lerp(a.ScrimBottom, b.ScrimBottom, t),
            CardTop: OkLab.Lerp(a.CardTop, b.CardTop, t),
            CardBottom: OkLab.Lerp(a.CardBottom, b.CardBottom, t),
            EdgeTop: OkLab.Lerp(a.EdgeTop, b.EdgeTop, t),
            EdgeBottom: OkLab.Lerp(a.EdgeBottom, b.EdgeBottom, t),
            InnerHighlight: OkLab.Lerp(a.InnerHighlight, b.InnerHighlight, t),
            Shadow: OkLab.Lerp(a.Shadow, b.Shadow, t),
            AmbientReflection: OkLab.Lerp(a.AmbientReflection, b.AmbientReflection, t),
            MasterCard: OkLab.Lerp(a.MasterCard, b.MasterCard, t),
            Subcard: OkLab.Lerp(a.Subcard, b.Subcard, t),
            ConsoleBar: OkLab.Lerp(a.ConsoleBar, b.ConsoleBar, t),
            TextPrimary: OkLab.Lerp(a.TextPrimary, b.TextPrimary, t),
            TextSecondary: OkLab.Lerp(a.TextSecondary, b.TextSecondary, t),
            KeyLightAngle: LerpAngle(a.KeyLightAngle, b.KeyLightAngle, t),
            SpecularGain: a.SpecularGain + (b.SpecularGain - a.SpecularGain) * t,
            IsDark: t >= 0.5 ? b.IsDark : a.IsDark
        );
    }

    private static double LerpAngle(double a, double b, double t)
    {
        double diff = (b - a + Math.PI) % (2.0 * Math.PI) - Math.PI;
        return a + diff * t;
    }

    private static bool HasMeaningfulDifference(CircadianFrame a, CircadianFrame b)
    {
        return Math.Abs(a.CardTop.R - b.CardTop.R) > 1 ||
               Math.Abs(a.CardTop.G - b.CardTop.G) > 1 ||
               Math.Abs(a.CardTop.B - b.CardTop.B) > 1 ||
               Math.Abs(a.ScrimMid.A - b.ScrimMid.A) > 1 ||
               Math.Abs(a.KeyLightAngle - b.KeyLightAngle) > 0.02;
    }

    private void ApplyFrame(CircadianFrame frame)
    {
        // Contrast guard: ensure text contrast ratio is at least 4.5:1
        var scrimMid = GuardTextContrast(frame.TextPrimary, frame.CardTop, frame.ScrimMid);

        _scrimBrush.GradientStops[0].Color = frame.ScrimTop;
        _scrimBrush.GradientStops[1].Color = scrimMid;
        _scrimBrush.GradientStops[2].Color = frame.ScrimBottom;

        _cardBrush.GradientStops[0].Color = frame.CardTop;
        _cardBrush.GradientStops[1].Color = frame.CardBottom;

        // Hover slightly brightens card top & bottom
        _cardHoverBrush.GradientStops[0].Color = Color.FromArgb(
            (byte)Math.Clamp(frame.CardTop.A + 20, 0, 255),
            frame.CardTop.R, frame.CardTop.G, frame.CardTop.B);
        _cardHoverBrush.GradientStops[1].Color = Color.FromArgb(
            (byte)Math.Clamp(frame.CardBottom.A + 20, 0, 255),
            frame.CardBottom.R, frame.CardBottom.G, frame.CardBottom.B);

        _edgeBrush.GradientStops[0].Color = frame.EdgeTop;
        _edgeBrush.GradientStops[1].Color = OkLab.Lerp(frame.EdgeTop, frame.EdgeBottom, 0.45);
        _edgeBrush.GradientStops[2].Color = frame.EdgeBottom;

        _edgeHoverBrush.GradientStops[0].Color = Color.FromArgb(
            (byte)Math.Clamp(frame.EdgeTop.A + 30, 0, 255),
            frame.EdgeTop.R, frame.EdgeTop.G, frame.EdgeTop.B);
        _edgeHoverBrush.GradientStops[1].Color = frame.EdgeTop;
        _edgeHoverBrush.GradientStops[2].Color = frame.EdgeBottom;

        _innerHighlightBrush.Color = frame.InnerHighlight;
        _ambientReflectionBrush.Color = frame.AmbientReflection;
        _masterCardBrush.Color = frame.MasterCard;
        _subcardBrush.Color = frame.Subcard;
        _consoleBarBrush.Color = frame.ConsoleBar;
        _textPrimaryBrush.Color = frame.TextPrimary;
        _textSecondaryBrush.Color = frame.TextSecondary;

        CurrentKeyLightAngle = frame.KeyLightAngle;
        CurrentSpecularGain = frame.SpecularGain;

        // Synchronize RequestedThemeVariant if dark/light status flipped
        if (_app != null && _previewMinute == null)
        {
            var targetVariant = frame.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
            if (_app.RequestedThemeVariant != targetVariant)
            {
                _app.RequestedThemeVariant = targetVariant;
            }
        }

        PhaseChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Color GuardTextContrast(Color text, Color card, Color scrim)
    {
        double textLum = SoftPalette.Luminance(text);
        double cardLum = SoftPalette.Luminance(card);
        double contrast = (Math.Max(textLum, cardLum) + 0.05) / (Math.Min(textLum, cardLum) + 0.05);

        if (contrast < 4.5)
        {
            byte adjustedA = (byte)Math.Clamp(scrim.A * 1.25, 40, 240);
            return Color.FromArgb(adjustedA, scrim.R, scrim.G, scrim.B);
        }
        return scrim;
    }

    private void ApplyVariantDefaults()
    {
        bool isDark = _app?.ActualThemeVariant == ThemeVariant.Dark;
        ApplyFrame(isDark ? NightFrame : MorningFrame);
    }
}

