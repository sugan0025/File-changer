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

    // Geometry caches for the 5 SVG paths (Canvas: 1080 x 340)
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
        // Path 1: M 240 170 L 340 170 (between Input and Processing)
        using (var ctx = _geomPath1.Open())
        {
            ctx.BeginFigure(new Point(240, 170), false);
            ctx.LineTo(new Point(340, 170));
            ctx.EndFigure(false);
        }

        // Path 2: M 520 170 L 620 170 (between Processing and Routing)
        using (var ctx = _geomPath2.Open())
        {
            ctx.BeginFigure(new Point(520, 170), false);
            ctx.LineTo(new Point(620, 170));
            ctx.EndFigure(false);
        }

        // Path Top: M 800 170 C 830 170, 835 70, 865 70 (Routing to End Node 0)
        using (var ctx = _geomPathTop.Open())
        {
            ctx.BeginFigure(new Point(800, 170), false);
            ctx.CubicBezierTo(new Point(830, 170), new Point(835, 70), new Point(865, 70));
            ctx.EndFigure(false);
        }

        // Path Mid: M 800 170 L 865 170 (Routing to End Node 1)
        using (var ctx = _geomPathMid.Open())
        {
            ctx.BeginFigure(new Point(800, 170), false);
            ctx.LineTo(new Point(865, 170));
            ctx.EndFigure(false);
        }

        // Path Bot: M 800 170 C 830 170, 835 270, 865 270 (Routing to End Node 2)
        using (var ctx = _geomPathBot.Open())
        {
            ctx.BeginFigure(new Point(800, 170), false);
            ctx.CubicBezierTo(new Point(830, 170), new Point(835, 270), new Point(865, 270));
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

        // Particle configuration:
        // speed = 3.0s, count = 3 particles per path, size = 2.5
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

            // 1. Path 1: (240, 170) -> (340, 170)
            Point pt1 = new(240 + 100 * p, 170);
            DrawParticle(context, pt1, activeCoreColor, activeGlowColor);

            // 2. Path 2: (520, 170) -> (620, 170)
            Point pt2 = new(520 + 100 * p, 170);
            DrawParticle(context, pt2, activeCoreColor, activeGlowColor);

            // 3. Path Top: M 800 170 C 830 170, 835 70, 865 70
            double u = 1.0 - p;
            double xt = u * u * u * 800 + 3 * u * u * p * 830 + 3 * u * p * p * 835 + p * p * p * 865;
            double yt = u * u * u * 170 + 3 * u * u * p * 170 + 3 * u * p * p * 70 + p * p * p * 70;
            bool isTopActive = ActiveIndex == 0;
            DrawParticle(context, new Point(xt, yt),
                isTopActive ? activeCoreColor : normalCoreColor,
                isTopActive ? activeGlowColor : normalGlowColor);

            // 4. Path Mid: (800, 170) -> (865, 170)
            Point pm = new(800 + 65 * p, 170);
            bool isMidActive = ActiveIndex == 1;
            DrawParticle(context, pm,
                isMidActive ? activeCoreColor : normalCoreColor,
                isMidActive ? activeGlowColor : normalGlowColor);

            // 5. Path Bot: M 800 170 C 830 170, 835 270, 865 270
            double xb = u * u * u * 800 + 3 * u * u * p * 830 + 3 * u * p * p * 835 + p * p * p * 865;
            double yb = u * u * u * 170 + 3 * u * u * p * 170 + 3 * u * p * p * 270 + p * p * p * 270;
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
