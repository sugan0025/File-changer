using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Filee.App.Services;

namespace Filee.App.Controls;

public partial class EnginePipelineControl : UserControl
{
    public static readonly StyledProperty<int> ActiveIndexProperty =
        AvaloniaProperty.Register<EnginePipelineControl, int>(
            nameof(ActiveIndex), 1, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsDropdownOpenProperty =
        AvaloniaProperty.Register<EnginePipelineControl, bool>(
            nameof(IsDropdownOpen), false, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsConvertingProperty =
        AvaloniaProperty.Register<EnginePipelineControl, bool>(
            nameof(IsConverting), false);

    public int ActiveIndex
    {
        get => GetValue(ActiveIndexProperty);
        set => SetValue(ActiveIndexProperty, value);
    }

    public bool IsDropdownOpen
    {
        get => GetValue(IsDropdownOpenProperty);
        set => SetValue(IsDropdownOpenProperty, value);
    }

    public bool IsConverting
    {
        get => GetValue(IsConvertingProperty);
        set => SetValue(IsConvertingProperty, value);
    }

    private readonly DispatcherTimer _cycleTimer;
    private readonly Stopwatch _dotWatch = Stopwatch.StartNew();
    private readonly Action<TimeSpan> _dotFrameCallback;
    private TopLevel? _topLevel;
    private Window? _window;
    private bool _isAnimRunning;

    public EnginePipelineControl()
    {
        _dotFrameCallback = OnAnimationFrame;
        InitializeComponent();

        _cycleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3.5)
        };
        _cycleTimer.Tick += OnCycleTick;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActiveIndexProperty)
        {
            int index = change.GetNewValue<int>();
            Canvas.ActiveIndex = index;
            UpdateActiveNodeVisuals();
        }
        else if (change.Property == IsConvertingProperty)
        {
            bool converting = change.GetNewValue<bool>();
            Canvas.IsConverting = converting;
        }
        else if (change.Property == IsDropdownOpenProperty)
        {
            bool isOpen = change.GetNewValue<bool>();
            if (isOpen)
            {
                _cycleTimer.Stop(); // Pause auto-rotation when user is inspecting engines
            }
            else
            {
                _cycleTimer.Start();
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _window = _topLevel as Window;

        if (_window != null)
        {
            _window.Activated += OnWindowActivated;
            _window.Deactivated += OnWindowDeactivated;
        }

        ActualThemeVariantChanged += OnThemeVariantChanged;

        if (AppHost.TryGet<CircadianService>() is { } circ)
        {
            circ.PhaseChanged += OnCircadianPhaseChanged;
        }

        UpdateThemeVisuals();
        UpdateActiveNodeVisuals();

        if (!IsDropdownOpen)
            _cycleTimer.Start();

        StartDotAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window != null)
        {
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }

        ActualThemeVariantChanged -= OnThemeVariantChanged;

        if (AppHost.TryGet<CircadianService>() is { } circ)
        {
            circ.PhaseChanged -= OnCircadianPhaseChanged;
        }

        _cycleTimer.Stop();
        _isAnimRunning = false;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (!IsDropdownOpen && !_cycleTimer.IsEnabled)
            _cycleTimer.Start();
        StartDotAnimation();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _cycleTimer.Stop();
        _isAnimRunning = false;
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        UpdateThemeVisuals();
        UpdateActiveNodeVisuals();
    }

    private void OnCircadianPhaseChanged(object? sender, EventArgs e)
    {
        UpdateThemeVisuals();
        UpdateActiveNodeVisuals();
    }

    private void StartDotAnimation()
    {
        if (_isAnimRunning || _topLevel is null || !Motion.Enabled || !IsEffectivelyVisible || (_window != null && !_window.IsActive))
        {
            if (!Motion.Enabled)
            {
                Dot0.Opacity = 0.8;
                Dot1.Opacity = 0.8;
                Dot2.Opacity = 0.8;
            }
            return;
        }

        _isAnimRunning = true;
        _topLevel.RequestAnimationFrame(_dotFrameCallback);
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        if (!_isAnimRunning || _topLevel is null)
            return;

        if (!Motion.Enabled || !IsEffectivelyVisible || (_window != null && !_window.IsActive))
        {
            _isAnimRunning = false;
            Dot0.Opacity = 0.8;
            Dot1.Opacity = 0.8;
            Dot2.Opacity = 0.8;
            return;
        }

        // Animate the 3 processing dots with staggered sinusoidal opacity
        // 1.5s wave period matching Framer Motion: [0.3, 1, 0.3]
        double t = _dotWatch.Elapsed.TotalSeconds;
        const double omega = 2.0 * Math.PI / 1.5; // ~4.188 rad/s

        Dot0.Opacity = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin(t * omega));
        Dot1.Opacity = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin((t - 0.2) * omega));
        Dot2.Opacity = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin((t - 0.4) * omega));

        _topLevel.RequestAnimationFrame(_dotFrameCallback);
    }

    private void OnCycleTick(object? sender, EventArgs e)
    {
        if (!IsDropdownOpen)
            ActiveIndex = (ActiveIndex + 1) % 3;
    }

    private void OnNode0Pressed(object? sender, PointerPressedEventArgs e) => SelectNode(0);
    private void OnNode1Pressed(object? sender, PointerPressedEventArgs e) => SelectNode(1);
    private void OnNode2Pressed(object? sender, PointerPressedEventArgs e) => SelectNode(2);

    private void SelectNode(int index)
    {
        ActiveIndex = index;
        IsDropdownOpen = true;

        AppHost.TryGet<SoundService>()?.PlayFanOut();
    }

    private void UpdateThemeVisuals()
    {
        bool isDark = ActualThemeVariant == ThemeVariant.Dark;

        if (isDark)
        {
            // Routing Hero card is high-contrast liquid white glass in Dark mode
            RoutingCard.Background = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));
            RoutingCard.BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
            RoutingLabel.Foreground = new SolidColorBrush(Color.Parse("#4B5563"));
            RoutingTitle.Foreground = new SolidColorBrush(Color.Parse("#111827"));

            var dotBrush = new SolidColorBrush(Color.Parse("#374151"));
            Dot0.Fill = dotBrush;
            Dot1.Fill = dotBrush;
            Dot2.Fill = dotBrush;

            Node1Card.Background = new SolidColorBrush(Color.FromArgb(55, 24, 28, 40));
            Node1Card.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
            Node2Card.Background = new SolidColorBrush(Color.FromArgb(55, 24, 28, 40));
            Node2Card.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
        }
        else
        {
            // Routing Hero card is liquid charcoal glass in Light mode
            RoutingCard.Background = new SolidColorBrush(Color.FromArgb(230, 18, 22, 32));
            RoutingCard.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0));
            RoutingLabel.Foreground = new SolidColorBrush(Color.Parse("#9CA3AF"));
            RoutingTitle.Foreground = new SolidColorBrush(Color.Parse("#FFFFFF"));

            var dotBrush = new SolidColorBrush(Color.Parse("#9CA3AF"));
            Dot0.Fill = dotBrush;
            Dot1.Fill = dotBrush;
            Dot2.Fill = dotBrush;

            Node1Card.Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
            Node1Card.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
            Node2Card.Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
            Node2Card.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
        }
    }

    private void UpdateActiveNodeVisuals()
    {
        bool isDark = ActualThemeVariant == ThemeVariant.Dark;

        UpdateEndNode(EndNode0, DotNode0, TextNode0, ActiveIndex == 0, isDark);
        UpdateEndNode(EndNode1, DotNode1, TextNode1, ActiveIndex == 1, isDark);
        UpdateEndNode(EndNode2, DotNode2, TextNode2, ActiveIndex == 2, isDark);
    }

    private void UpdateEndNode(GlassCard card, Border dot, TextBlock label, bool isActive, bool isDark)
    {
        if (isActive)
        {
            // Active Liquid Glass highlight
            card.Background = isDark
                ? new SolidColorBrush(Color.FromArgb(140, 36, 44, 62))
                : new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));
            card.BorderBrush = isDark
                ? new SolidColorBrush(Color.FromArgb(220, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(180, 17, 24, 39));
            card.BorderThickness = new Thickness(1.5);
            card.BoxShadow = BoxShadows.Parse(isDark
                ? "0 10 24 0 #40000000, 0 1 0 0 #80FFFFFF, inset 0 1 1 0 #40FFFFFF"
                : "0 8 20 0 #25000000, 0 1 0 0 #35000000");

            label.FontWeight = FontWeight.SemiBold;
            label.Foreground = isDark
                ? new SolidColorBrush(Color.Parse("#FFFFFF"))
                : new SolidColorBrush(Color.Parse("#111827"));

            dot.Width = 11;
            dot.Height = 11;
            dot.Background = isDark
                ? new SolidColorBrush(Color.Parse("#FFFFFF"))
                : new SolidColorBrush(Color.Parse("#111827"));
        }
        else
        {
            // Inactive Liquid Glass surface
            card.Background = isDark
                ? new SolidColorBrush(Color.FromArgb(45, 20, 25, 36))
                : new SolidColorBrush(Color.FromArgb(160, 245, 247, 250));
            card.BorderBrush = isDark
                ? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
            card.BorderThickness = new Thickness(1.0);
            card.BoxShadow = BoxShadows.Parse(isDark
                ? "0 6 16 0 #20000000, 0 1 0 0 #25FFFFFF, inset 0 1 0 0 #10FFFFFF"
                : "0 4 12 0 #15000000");

            label.FontWeight = FontWeight.Medium;
            label.Foreground = isDark
                ? new SolidColorBrush(Color.FromArgb(200, 156, 163, 175))
                : new SolidColorBrush(Color.Parse("#6B7280"));

            dot.Width = 9;
            dot.Height = 9;
            dot.Background = isDark
                ? new SolidColorBrush(Color.FromArgb(120, 156, 163, 175))
                : new SolidColorBrush(Color.Parse("#D1D5DB"));
        }
    }
}
