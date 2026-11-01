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

    private readonly DispatcherTimer _cycleTimer;
    private readonly Stopwatch _dotWatch = Stopwatch.StartNew();
    private TopLevel? _topLevel;
    private bool _isAnimRunning;

    public EnginePipelineControl()
    {
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

        UpdateThemeVisuals();
        UpdateActiveNodeVisuals();

        if (!IsDropdownOpen)
            _cycleTimer.Start();

        StartDotAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _cycleTimer.Stop();
        _isAnimRunning = false;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void StartDotAnimation()
    {
        if (_isAnimRunning || _topLevel is null)
            return;

        _isAnimRunning = true;
        _topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        if (!_isAnimRunning || _topLevel is null)
            return;

        // Animate the 3 processing dots with staggered sinusoidal opacity
        // 1.5s wave period matching Framer Motion: [0.3, 1, 0.3]
        double t = _dotWatch.Elapsed.TotalSeconds;
        const double omega = 2.0 * Math.PI / 1.5; // ~4.188 rad/s

        Dot0.Opacity = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin(t * omega));
        Dot1.Opacity = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin((t - 0.2) * omega));
        Dot2.Opacity = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin((t - 0.4) * omega));

        _topLevel.RequestAnimationFrame(OnAnimationFrame);
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
        // When clicking the category button, switch to it, open the dropdown, and play sound
        ActiveIndex = index;
        IsDropdownOpen = true;

        AppHost.TryGet<SoundService>()?.PlayFanOut();
    }

    private void UpdateThemeVisuals()
    {
        bool isDark = ActualThemeVariant == ThemeVariant.Dark;

        if (isDark)
        {
            // Routing Hero card is high-contrast INVERTED (White in Dark mode)
            RoutingCard.Background = new SolidColorBrush(Color.Parse("#FFFFFF"));
            RoutingCard.BorderBrush = new SolidColorBrush(Color.Parse("#E5E7EB"));
            RoutingLabel.Foreground = new SolidColorBrush(Color.Parse("#6B7280"));
            RoutingTitle.Foreground = new SolidColorBrush(Color.Parse("#111827"));

            var dotBrush = new SolidColorBrush(Color.Parse("#4B5563"));
            Dot0.Fill = dotBrush;
            Dot1.Fill = dotBrush;
            Dot2.Fill = dotBrush;
        }
        else
        {
            // Routing Hero card is high-contrast INVERTED (Charcoal in Light mode)
            RoutingCard.Background = new SolidColorBrush(Color.Parse("#111111"));
            RoutingCard.BorderBrush = new SolidColorBrush(Color.Parse("#222222"));
            RoutingLabel.Foreground = new SolidColorBrush(Color.Parse("#9CA3AF"));
            RoutingTitle.Foreground = new SolidColorBrush(Color.Parse("#FFFFFF"));

            var dotBrush = new SolidColorBrush(Color.Parse("#9CA3AF"));
            Dot0.Fill = dotBrush;
            Dot1.Fill = dotBrush;
            Dot2.Fill = dotBrush;
        }
    }

    private void UpdateActiveNodeVisuals()
    {
        bool isDark = ActualThemeVariant == ThemeVariant.Dark;

        UpdateEndNode(EndNode0, DotNode0, TextNode0, ActiveIndex == 0, isDark);
        UpdateEndNode(EndNode1, DotNode1, TextNode1, ActiveIndex == 1, isDark);
        UpdateEndNode(EndNode2, DotNode2, TextNode2, ActiveIndex == 2, isDark);
    }

    private void UpdateEndNode(Border card, Border dot, TextBlock label, bool isActive, bool isDark)
    {
        if (isActive)
        {
            card.Background = isDark ? new SolidColorBrush(Color.Parse("#1A1D2B")) : new SolidColorBrush(Color.Parse("#FFFFFF"));
            card.BorderBrush = isDark ? new SolidColorBrush(Color.Parse("#E5E7EB")) : new SolidColorBrush(Color.Parse("#1F2937"));
            card.BorderThickness = new Thickness(1.5);
            label.FontWeight = FontWeight.SemiBold;
            label.Foreground = isDark ? new SolidColorBrush(Color.Parse("#FFFFFF")) : new SolidColorBrush(Color.Parse("#111827"));

            dot.Width = 11;
            dot.Height = 11;
            dot.Background = isDark ? new SolidColorBrush(Color.Parse("#FFFFFF")) : new SolidColorBrush(Color.Parse("#111827"));
        }
        else
        {
            card.Background = isDark ? new SolidColorBrush(Color.Parse("#10121A")) : new SolidColorBrush(Color.Parse("#F8F9FA"));
            card.BorderBrush = isDark ? new SolidColorBrush(Color.Parse("#2D3748")) : new SolidColorBrush(Color.Parse("#E5E7EB"));
            card.BorderThickness = new Thickness(1.0);
            label.FontWeight = FontWeight.Medium;
            label.Foreground = isDark ? new SolidColorBrush(Color.Parse("#9CA3AF")) : new SolidColorBrush(Color.Parse("#6B7280"));

            dot.Width = 9;
            dot.Height = 9;
            dot.Background = isDark ? new SolidColorBrush(Color.Parse("#4B5563")) : new SolidColorBrush(Color.Parse("#D1D5DB"));
        }
    }
}
