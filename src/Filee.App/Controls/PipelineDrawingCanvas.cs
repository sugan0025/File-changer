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
/// traveling continuously along straight and sweeping cubic Bézier paths (⚟).
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

    // Geometry caches for the SVG paths (Canvas: 1180 x 350)
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
        // Path 1: M 220 175 L 290 175 (Input right edge to Processing left edge)
        using (var ctx = _geomPath1.Open())
        {
            ctx.BeginFigure(new Point(220, 175), false);
            ctx.LineTo(new Point(290, 175));
            ctx.EndFigure(false);
        }

        // Path 2: M 460 175 L 530 175 (Processing right edge to Routing left edge)
        using (var ctx = _geomPath2.Open())
        {
            ctx.BeginFigure(new Point(460, 175), false);
            ctx.LineTo(new Point(530, 175));
            ctx.EndFigure(false);
        }

        // Long Branching Lines ⚟ (Routing right edge at 700 to End Nodes left edge at 890: 190px span!)

        // Path Top: M 700 175 C 775 175, 815 75, 890 75
        using (var ctx = _geomPathTop.Open())
        {
            ctx.BeginFigure(new Point(700, 175), false);
            ctx.CubicBezierTo(new Point(775, 175), new Point(815, 75), new Point(890, 75));
            ctx.EndFigure(false);
        }

        // Path Mid: M 700 175 L 890 175
        using (var ctx = _geomPathMid.Open())
        {
            ctx.BeginFigure(new Point(700, 175), false);
            ctx.LineTo(new Point(890, 175));
            ctx.EndFigure(false);
        }

        // Path Bot: M 700 175 C 775 175, 815 275, 890 275
        using (var ctx = _geomPathBot.Open())
        {
            ctx.BeginFigure(new Point(700, 175), false);
            ctx.CubicBezierTo(new Point(775, 175), new Point(815, 275), new Point(890, 275));
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

        // Subtle glowing dashed line pen (liquid glass aesthetic)
        var dashColor = isDark
            ? Color.FromArgb(145, 95, 110, 135)   // luminous slate
            : Color.FromArgb(175, 195, 205, 220); // soft frosted grey
        var dashPen = new Pen(new SolidColorBrush(dashColor), 1.5, s_dashStyle);

        // Draw the static connecting paths
        context.DrawGeometry(null, dashPen, _geomPath1);
        context.DrawGeometry(null, dashPen, _geomPath2);
        context.DrawGeometry(null, dashPen, _geomPathTop);
        context.DrawGeometry(null, dashPen, _geomPathMid);
        context.DrawGeometry(null, dashPen, _geomPathBot);

        // Particle configuration: 3 particles per path, 3.0s cycle
        const double speed = 3.0;
        const int count = 3;
        const double interval = 1.0 / count;
        double elapsed = _stopwatch.Elapsed.TotalSeconds;
        double baseProgress = (elapsed / speed) % 1.0;

        // Colors for particles
        Color normalCoreColor = isDark ? Color.FromRgb(170, 185, 205) : Color.FromRgb(75, 85, 99);
        Color activeCoreColor = isDark ? Color.FromRgb(255, 255, 255) : Color.FromRgb(17, 24, 39);
        Color normalGlowColor = isDark ? Color.FromArgb(95, 160, 180, 215) : Color.FromArgb(70, 107, 114, 128);
        Color activeGlowColor = isDark ? Color.FromArgb(200, 255, 255, 255) : Color.FromArgb(150, 31, 41, 55);

        for (int i = 0; i < count; i++)
        {
            double p = (baseProgress + i * interval) % 1.0;

            // 1. Path 1: (220, 175) -> (290, 175)
            Point pt1 = new(220 + 70 * p, 175);
            DrawParticle(context, pt1, activeCoreColor, activeGlowColor);

            // 2. Path 2: (460, 175) -> (530, 175)
            Point pt2 = new(460 + 70 * p, 175);
            DrawParticle(context, pt2, activeCoreColor, activeGlowColor);

            // 3. Path Top: M 700 175 C 775 175, 815 75, 890 75 (190px span!)
            double u = 1.0 - p;
            double xt = u * u * u * 700 + 3 * u * u * p * 775 + 3 * u * p * p * 815 + p * p * p * 890;
            double yt = u * u * u * 175 + 3 * u * u * p * 175 + 3 * u * p * p * 75 + p * p * p * 75;
            bool isTopActive = ActiveIndex == 0;
            DrawParticle(context, new Point(xt, yt),
                isTopActive ? activeCoreColor : normalCoreColor,
                isTopActive ? activeGlowColor : normalGlowColor);

            // 4. Path Mid: M 700 175 L 890 175 (190px span!)
            Point pm = new(700 + 190 * p, 175);
            bool isMidActive = ActiveIndex == 1;
            DrawParticle(context, pm,
                isMidActive ? activeCoreColor : normalCoreColor,
                isMidActive ? activeGlowColor : normalGlowColor);

            // 5. Path Bot: M 700 175 C 775 175, 815 275, 890 275 (190px span!)
            double xb = u * u * u * 700 + 3 * u * u * p * 775 + 3 * u * p * p * 815 + p * p * p * 890;
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
        context.DrawEllipse(outerGlowBrush, null, center, 7.0, 7.0);

        // Mid glow halo
        var midGlowBrush = new SolidColorBrush(Color.FromArgb((byte)(glowColor.A * 0.70), glowColor.R, glowColor.G, glowColor.B));
        context.DrawEllipse(midGlowBrush, null, center, 4.5, 4.5);

        // Solid inner core (size 2.5 radius)
        var coreBrush = new SolidColorBrush(coreColor);
        context.DrawEllipse(coreBrush, null, center, 2.5, 2.5);
    }
}
