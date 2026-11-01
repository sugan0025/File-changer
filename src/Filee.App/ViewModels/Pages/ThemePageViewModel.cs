// Theme: light/dark, accent colour, softness (corner radius), the donut's opacity, reduce animations, and the window
// size (UiZoom). Changes apply live.

using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Settings;

namespace Filee.App.ViewModels.Pages;

public sealed partial class ThemePageViewModel : ObservableObject, IDisposable
{
    private readonly UserDataStore _store;
    private readonly ILocalizer _loc;
    private readonly bool _loading;
    private bool _settingPresetOrMode;

    public ThemePageViewModel(UserDataStore store, ILocalizer loc, IPlatformServices platform)
    {
        _store = store;
        _loc = loc;
        _loading = true;
        var theme = store.Settings.Theme;
        Styles = Enum.GetValues<ThemeStyle>().Select(s => new Choice<ThemeStyle>(s, loc[$"theme.style.{s}"])).ToList();
        Style = Styles.FirstOrDefault(s => s.Value == theme.Style) ?? Styles[0];
        Modes = Enum.GetValues<ThemeMode>().Select(m => new Choice<ThemeMode>(m, loc[$"theme.mode.{m}"])).ToList();
        Mode = Modes.First(m => m.Value == theme.Mode);

        CircadianModes = Enum.GetValues<CircadianMode>().Select(m => new Choice<CircadianMode>(m, loc[$"theme.circadian.mode.{m}"])).ToList();
        CircadianMode = CircadianModes.FirstOrDefault(m => m.Value == theme.Circadian.Mode) ?? CircadianModes[0];
        CircadianPhases = Enum.GetValues<CircadianPhase>().Select(p => new Choice<CircadianPhase>(p, loc[$"theme.circadian.phase.{p}"])).ToList();
        PinnedPhase = CircadianPhases.FirstOrDefault(p => p.Value == (theme.Circadian.Pinned ?? CircadianPhase.Morning));

        var now = DateTime.Now;
        _scrubberMinute = now.Hour * 60 + now.Minute;
        UpdatePhaseText();

        if (AppHost.TryGet<CircadianService>() is { } circadian)
            circadian.PhaseChanged += OnCircadianPhaseChanged;

        if (!string.IsNullOrEmpty(theme.PresetKey) &&
            ThemeService.Presets.FirstOrDefault(p => string.Equals(p.Key, theme.PresetKey, StringComparison.OrdinalIgnoreCase)) is { } initialPreset)
        {
            var isDark = theme.Mode == ThemeMode.Dark ||
                         (theme.Mode == ThemeMode.System && Application.Current?.ActualThemeVariant == ThemeVariant.Dark);
            Accent = initialPreset.ActiveColor(isDark);
        }
        else
        {
            Accent = Color.TryParse(theme.Accent, out var c) ? c : Colors.MediumPurple;
        }
        AccentHex = $"#{Accent.R:X2}{Accent.G:X2}{Accent.B:X2}";
        CornerRadius = theme.CornerRadius;
        DonutOpacity = Math.Round(Math.Clamp(store.Settings.Donut.Opacity, DonutSettings.MinOpacity, 1) * 100);
        ReduceMotion = theme.ReduceMotion ?? platform.PrefersReducedMotion;
        PreviewItems =
        [
            new DonutItem { Id = "a", Label = "PNG" },
            new DonutItem { Id = "b", Label = "PDF" },
            new DonutItem { Id = "c", Label = "WEBP" },
            new DonutItem { Id = "d", Label = "HWPX" },
            new DonutItem { Id = "e", Label = "JPG 70%", Caption = "JPG" },
            new DonutItem { Id = "f", Label = "TIFF" },
        ];
        _loading = false;
        UiZoom.Changed += OnZoomChanged;
        loc.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<Choice<ThemeStyle>> Styles { get; }
    public IReadOnlyList<Choice<ThemeMode>> Modes { get; }
    public IReadOnlyList<Choice<CircadianMode>> CircadianModes { get; }
    public IReadOnlyList<Choice<CircadianPhase>> CircadianPhases { get; }
    public IReadOnlyList<Color> Swatches { get; } = ThemeService.Swatches.Select(Color.Parse).ToList();
    public IReadOnlyList<DonutItem> PreviewItems { get; }

    public sealed record PresetItem(string Key, string Name, Color Color);

    public IReadOnlyList<PresetItem> PastelPresets =>
        ThemeService.Presets.Select(p => new PresetItem(
            p.Key,
            _loc[$"theme.preset.{p.Key}"],
            p.ActiveColor(IsDarkNow))).ToList();

    private bool IsDarkNow =>
        Mode?.Value == ThemeMode.Dark ||
        (Mode?.Value == ThemeMode.System && Application.Current?.ActualThemeVariant == ThemeVariant.Dark);

    public bool IsMaterialYou => Style?.Value == ThemeStyle.MaterialYou;
    public bool IsCircadianActive => CircadianMode?.Value != Filee.Core.Settings.CircadianMode.Off;
    public bool IsPinnedMode => CircadianMode?.Value == Filee.Core.Settings.CircadianMode.Pinned;
    public string ScrubberTimeText => TimeSpan.FromMinutes(ScrubberMinute).ToString(@"hh\:mm");

    [ObservableProperty] private Choice<ThemeStyle> _style = null!;
    [ObservableProperty] private Choice<ThemeMode> _mode = null!;
    [ObservableProperty] private Choice<CircadianMode> _circadianMode = null!;
    [ObservableProperty] private Choice<CircadianPhase>? _pinnedPhase;
    [ObservableProperty] private double _scrubberMinute;
    [ObservableProperty] private bool _isScrubbing;
    [ObservableProperty] private string _currentPhaseText = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccentBrush), nameof(PreviewBackdrop))] private Color _accent;
    [ObservableProperty] private string _accentHex = "";

    public IBrush AccentBrush => new SolidColorBrush(Accent);

    /// <summary>
    /// What the preview donut floats on, standing in for the desktop: a gradient of the accent, so the slices'
    /// opacity shows (on the plain card, slices of its own colour look the same at any opacity).
    /// </summary>
    public IBrush PreviewBackdrop => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(ThemeService.Shade(Accent, 0.55), 0),
            new GradientStop(Accent, 0.5),
            new GradientStop(ThemeService.Shade(Accent, -0.45), 1),
        },
    };
    [ObservableProperty] private double _cornerRadius;
    [ObservableProperty] private bool _reduceMotion;

    /// <summary>
    /// The donut toolbar's opacity in percent: the one setting Donut toolbar → Appearance → Opacity edits too (each
    /// page reads it when it opens). The preview donut follows it at once, the toolbar the next time it opens.
    /// </summary>
    [ObservableProperty] private double _donutOpacity;

    /// <summary>The window size in percent (<see cref="UiZoom.Scale"/>): the same for every window, saved by UiZoom.</summary>
    public double WindowSize => Math.Round(UiZoom.Scale * 100);

    /// <summary>Where the shortcuts do the same, with the platform's key: Ctrl, or Cmd on a Mac.</summary>
    public string WindowSizeHint => _loc.Format("theme.window_size_hint", OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl");

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut() => UiZoom.ZoomOut();

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn() => UiZoom.ZoomIn();

    [RelayCommand(CanExecute = nameof(CanResetZoom))]
    private void ResetZoom() => UiZoom.Reset();

    private static bool CanZoomOut() => UiZoom.Scale > UiZoom.Min + 1e-9;
    private static bool CanZoomIn() => UiZoom.Scale < UiZoom.Max - 1e-9;
    private static bool CanResetZoom() => Math.Abs(UiZoom.Scale - 1) > 1e-9;

    // Ctrl + wheel in any window changes it too.
    private void OnZoomChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(WindowSize));
        ZoomOutCommand.NotifyCanExecuteChanged();
        ZoomInCommand.NotifyCanExecuteChanged();
        ResetZoomCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(WindowSizeHint));
        UpdatePhaseText();
    }

    private void OnCircadianPhaseChanged(object? sender, EventArgs e) => UpdatePhaseText();

    private void UpdatePhaseText()
    {
        var service = AppHost.TryGet<CircadianService>();
        var phase = service?.CurrentPhase ?? CircadianPhase.Night;
        CurrentPhaseText = _loc[$"theme.circadian.phase.{phase}"];
    }

    public void Dispose()
    {
        UiZoom.Changed -= OnZoomChanged;
        _loc.LanguageChanged -= OnLanguageChanged;
        if (AppHost.TryGet<CircadianService>() is { } circadian)
        {
            circadian.PhaseChanged -= OnCircadianPhaseChanged;
            circadian.SetPreviewMinute(null);
        }
    }

    partial void OnStyleChanged(Choice<ThemeStyle>? oldValue, Choice<ThemeStyle> newValue)
    {
        OnPropertyChanged(nameof(IsMaterialYou));
        if (!Choices.Restore(oldValue, newValue, v => Style = v))
            Save(t => t.Style = newValue.Value);
    }

    partial void OnCircadianModeChanged(Choice<CircadianMode>? oldValue, Choice<CircadianMode> newValue)
    {
        OnPropertyChanged(nameof(IsCircadianActive));
        OnPropertyChanged(nameof(IsPinnedMode));
        if (!Choices.Restore(oldValue, newValue, v => CircadianMode = v))
        {
            Save(t => t.Circadian.Mode = newValue.Value);
            AppHost.TryGet<CircadianService>()?.Configure(_store.Settings.Theme.Circadian);
            UpdatePhaseText();
        }
    }

    partial void OnPinnedPhaseChanged(Choice<CircadianPhase>? oldValue, Choice<CircadianPhase>? newValue)
    {
        if (newValue != null && !_loading)
        {
            Save(t => t.Circadian.Pinned = newValue.Value);
            AppHost.TryGet<CircadianService>()?.Configure(_store.Settings.Theme.Circadian);
            UpdatePhaseText();
        }
    }

    partial void OnScrubberMinuteChanged(double value)
    {
        OnPropertyChanged(nameof(ScrubberTimeText));
        if (IsScrubbing)
            AppHost.TryGet<CircadianService>()?.SetPreviewMinute(value);
    }

    partial void OnIsScrubbingChanged(bool value)
    {
        if (value)
            AppHost.TryGet<CircadianService>()?.SetPreviewMinute(ScrubberMinute);
        else
            AppHost.TryGet<CircadianService>()?.SetPreviewMinute(null);
    }

    [RelayCommand]
    private void ResetScrubber()
    {
        IsScrubbing = false;
        var now = DateTime.Now;
        ScrubberMinute = now.Hour * 60 + now.Minute;
        AppHost.TryGet<CircadianService>()?.SetPreviewMinute(null);
    }

    partial void OnModeChanged(Choice<ThemeMode>? oldValue, Choice<ThemeMode> newValue)
    {
        OnPropertyChanged(nameof(PastelPresets));
        if (!Choices.Restore(oldValue, newValue, v => Mode = v))
        {
            _settingPresetOrMode = true;
            try
            {
                Save(t =>
                {
                    t.Mode = newValue.Value;
                    if (!string.IsNullOrEmpty(t.PresetKey) &&
                        ThemeService.Presets.FirstOrDefault(p => string.Equals(p.Key, t.PresetKey, StringComparison.OrdinalIgnoreCase)) is { } preset)
                    {
                        var isDark = newValue.Value == ThemeMode.Dark ||
                                     (newValue.Value == ThemeMode.System && Application.Current?.ActualThemeVariant == ThemeVariant.Dark);
                        var newColor = preset.ActiveColor(isDark);
                        t.Accent = $"#{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";
                        Accent = newColor;
                    }
                });
            }
            finally
            {
                _settingPresetOrMode = false;
            }
        }
    }

    [RelayCommand]
    private void PickPreset(PresetItem preset)
    {
        _settingPresetOrMode = true;
        try
        {
            Save(t => t.PresetKey = preset.Key);
            Accent = preset.Color;
        }
        finally
        {
            _settingPresetOrMode = false;
        }
    }

    [RelayCommand]
    private void PickSwatch(Color color)
    {
        Accent = color;
    }

    [RelayCommand]
    private void SetStyle(ThemeStyle target)
    {
        var match = Styles.FirstOrDefault(s => s.Value == target);
        if (match != null && Style != match)
            Style = match;
    }

    partial void OnAccentChanged(Color value)
    {
        var hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        if (!string.Equals(AccentHex, hex, StringComparison.OrdinalIgnoreCase))
            AccentHex = hex;
        if (!_loading && !_settingPresetOrMode)
        {
            Save(t =>
            {
                t.PresetKey = null;
                t.Accent = hex;
            });
        }
        else
        {
            Save(t => t.Accent = hex);
        }
    }

    // Custom colour typed as #RRGGBB; ignored until it parses.
    partial void OnAccentHexChanged(string value)
    {
        if (Color.TryParse(value.Trim(), out var color) && color != Accent)
        {
            Save(t => t.PresetKey = null);
            Accent = Color.FromRgb(color.R, color.G, color.B);
        }
    }
    partial void OnCornerRadiusChanged(double value) => Save(t => t.CornerRadius = Math.Round(value));
    partial void OnReduceMotionChanged(bool value) => Save(t => t.ReduceMotion = value);

    partial void OnDonutOpacityChanged(double value)
    {
        if (_loading)
            return;
        _store.Settings.Donut.Opacity = Math.Round(value) / 100;
        _store.SaveSettings();
    }

    private void Save(Action<ThemeSettings> change)
    {
        if (_loading)
            return;
        change(_store.Settings.Theme);
        _store.SaveSettings(); // App re-applies the theme on SettingsChanged
    }
}
