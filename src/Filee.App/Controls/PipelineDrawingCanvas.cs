using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Filee.App.Controls;

/// <summary>
/// Native high-performance vector canvas for the conversion pipeline.
/// Draws static dashed connection lines and smooth 60 FPS glowing particles
/// traveling continuously along straight and cubic Bézier paths.
/// </summary>
public sealed class PipelineDrawingCanvas : Control
{
    public static readonly StyledProperty<int> ActiveIndexProperty =
        AvaloniaProperty.Register<PipelineDrawingCanvas, int>(nameof(ActiveIndex), 1);

    public int ActiveIndex
    {
        get => GetValue(ActiveIndexProperty);
        set => SetValue(ActiveIndexProperty, value);
    }

    private static readonly DashStyle s_dashStyle = new([4, 4], 0);

    // Geometry caches for the 5 SVG paths
    private readonly StreamGeometry _geomPath1 = new();
    private readonly StreamGeometry _geomPath2 = new();
    private readonly StreamGeometry _geomPathTop = new();
    private readonly StreamGeometry _geomPathMid = new();
    private readonly StreamGeometry _geomPathBot = new();

    private TopLevel? _topLevel;
    private bool _isLooping;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    static PipelineDrawingCanvas()
    {
        AffectsRender<PipelineDrawingCanvas>(ActiveIndexProperty);
    }

    public PipelineDrawingCanvas()
    {
        ClipToBounds = false;
        BuildGeometries();
    }

    private void BuildGeometries()
    {
        // Path 1: M 230 175 L 320 175
        using (var ctx = _geomPath1.Open())
        {
            ctx.BeginFigure(new Point(230, 175), false);
            ctx.LineTo(new Point(320, 175));
            ctx.EndFigure(false);
        }

        // Path 2: M 480 175 L 570 175
        using (var ctx = _geomPath2.Open())
        {
            ctx.BeginFigure(new Point(480, 175), false);
            ctx.LineTo(new Point(570, 175));
            ctx.EndFigure(false);
        }

        // Path Top: M 730 175 C 760 175, 765 75, 800 75
        using (var ctx = _geomPathTop.Open())
        {
            ctx.BeginFigure(new Point(730, 175), false);
            ctx.CubicBezierTo(new Point(760, 175), new Point(765, 75), new Point(800, 75));
            ctx.EndFigure(false);
        }

        // Path Mid: M 730 175 L 800 175
        using (var ctx = _geomPathMid.Open())
        {
            ctx.BeginFigure(new Point(730, 175), false);
            ctx.LineTo(new Point(800, 175));
            ctx.EndFigure(false);
        }

        // Path Bot: M 730 175 C 760 175, 765 275, 800 275
        using (var ctx = _geomPathBot.Open())
        {
            ctx.BeginFigure(new Point(730, 175), false);
            ctx.CubicBezierTo(new Point(760, 175), new Point(765, 275), new Point(800, 275));
            ctx.EndFigure(false);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        StartLoop();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isLooping = false;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void StartLoop()
    {
        if (_isLooping || _topLevel is null)
            return;

        _isLooping = true;
        _topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        if (!_isLooping || _topLevel is null)
            return;

        InvalidateVisual();
        _topLevel.RequestAnimationFrame(OnFrame);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        bool isDark = ActualThemeVariant == ThemeVariant.Dark;

        // Subtle dashed line pen
        var dashColor = isDark
            ? Color.FromArgb(140, 75, 85, 99)    // gray-700
            : Color.FromArgb(170, 209, 213, 219); // gray-300
        var dashPen = new Pen(new SolidColorBrush(dashColor), 1.5, s_dashStyle);

        // Draw the static connecting lines
        context.DrawGeometry(null, dashPen, _geomPath1);
        context.DrawGeometry(null, dashPen, _geomPath2);
        context.DrawGeometry(null, dashPen, _geomPathTop);
        context.DrawGeometry(null, dashPen, _geomPathMid);
        context.DrawGeometry(null, dashPen, _geomPathBot);

        // Particle configuration matching React:
        // particleSpeed = 3.0s, particleCount = 3, size = 2.5
        const double speed = 3.0;
        const int count = 3;
        const double interval = 1.0 / count;
        double elapsed = _stopwatch.Elapsed.TotalSeconds;
        double baseProgress = (elapsed / speed) % 1.0;

        // Colors for particles
        Color normalCoreColor = isDark ? Color.FromRgb(156, 163, 175) : Color.FromRgb(75, 85, 99);
        Color activeCoreColor = isDark ? Color.FromRgb(255, 255, 255) : Color.FromRgb(17, 24, 39);
        Color normalGlowColor = isDark ? Color.FromArgb(90, 156, 163, 175) : Color.FromArgb(70, 107, 114, 128);
        Color activeGlowColor = isDark ? Color.FromArgb(180, 255, 255, 255) : Color.FromArgb(140, 31, 41, 55);

        for (int i = 0; i < count; i++)
        {
            double p = (baseProgress + i * interval) % 1.0;

            // 1. Path 1: (230, 175) -> (320, 175)
            Point pt1 = new(230 + 90 * p, 175);
            DrawParticle(context, pt1, activeCoreColor, activeGlowColor);

            // 2. Path 2: (480, 175) -> (570, 175)
            Point pt2 = new(480 + 90 * p, 175);
            DrawParticle(context, pt2, activeCoreColor, activeGlowColor);

            // 3. Path Top: M 730 175 C 760 175, 765 75, 800 75
            double u = 1.0 - p;
            double xt = u * u * u * 730 + 3 * u * u * p * 760 + 3 * u * p * p * 765 + p * p * p * 800;
            double yt = u * u * u * 175 + 3 * u * u * p * 175 + 3 * u * p * p * 75 + p * p * p * 75;
            bool isTopActive = ActiveIndex == 0;
            DrawParticle(context, new Point(xt, yt),
                isTopActive ? activeCoreColor : normalCoreColor,
                isTopActive ? activeGlowColor : normalGlowColor);

            // 4. Path Mid: (730, 175) -> (800, 175)
            Point pm = new(730 + 70 * p, 175);
            bool isMidActive = ActiveIndex == 1;
            DrawParticle(context, pm,
                isMidActive ? activeCoreColor : normalCoreColor,
                isMidActive ? activeGlowColor : normalGlowColor);

            // 5. Path Bot: M 730 175 C 760 175, 765 275, 800 275
            double xb = u * u * u * 730 + 3 * u * u * p * 760 + 3 * u * p * p * 765 + p * p * p * 800;
            double yb = u * u * u * 175 + 3 * u * u * p * 175 + 3 * u * p * p * 275 + p * p * p * 275;
            bool isBotActive = ActiveIndex == 2;
            DrawParticle(context, new Point(xb, yb),
                isBotActive ? activeCoreColor : normalCoreColor,
                isBotActive ? activeGlowColor : normalGlowColor);
        }
    }

    private static void DrawParticle(DrawingContext context, Point center, Color coreColor, Color glowColor)
    {
        // Outer soft glow (feGaussianBlur equivalent)
        var outerGlowBrush = new SolidColorBrush(Color.FromArgb((byte)(glowColor.A * 0.35), glowColor.R, glowColor.G, glowColor.B));
        context.DrawEllipse(outerGlowBrush, null, center, 6.5, 6.5);

        // Mid glow halo
        var midGlowBrush = new SolidColorBrush(Color.FromArgb((byte)(glowColor.A * 0.70), glowColor.R, glowColor.G, glowColor.B));
        context.DrawEllipse(midGlowBrush, null, center, 4.2, 4.2);

        // Solid inner core (size 2.5 radius)
        var coreBrush = new SolidColorBrush(coreColor);
        context.DrawEllipse(coreBrush, null, center, 2.5, 2.5);
    }
}

