using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Filee.App.Services;

namespace Filee.App.Controls;

/// <summary>
/// Native high-performance vector canvas for the conversion pipeline.
/// Draws static dashed connection lines, constant-velocity glowing comet particles
/// via arc-length parameterization, and zero per-frame heap allocations.
/// Automatically suspends the render loop when hidden, deactivated, or reduced motion is enabled.
/// </summary>
public sealed class PipelineDrawingCanvas : Control
{
    public static readonly StyledProperty<int> ActiveIndexProperty =
        AvaloniaProperty.Register<PipelineDrawingCanvas, int>(nameof(ActiveIndex), 1);

    public static readonly StyledProperty<bool> IsConvertingProperty =
        AvaloniaProperty.Register<PipelineDrawingCanvas, bool>(nameof(IsConverting), false);

    public int ActiveIndex
    {
        get => GetValue(ActiveIndexProperty);
        set => SetValue(ActiveIndexProperty, value);
    }

    public bool IsConverting
    {
        get => GetValue(IsConvertingProperty);
        set => SetValue(IsConvertingProperty, value);
    }

    // Static dashed pens & styles
    private static readonly DashStyle s_dashStyle = new([4, 4], 0);
    private static readonly Pen s_dashPenDark = new(new SolidColorBrush(Color.FromArgb(145, 95, 110, 135)), 1.5, s_dashStyle);
    private static readonly Pen s_dashPenLight = new(new SolidColorBrush(Color.FromArgb(175, 195, 205, 220)), 1.5, s_dashStyle);
    private static readonly Pen s_activeBranchPenDark = new(new SolidColorBrush(Color.FromArgb(200, 142, 133, 243)), 1.8);
    private static readonly Pen s_activeBranchPenLight = new(new SolidColorBrush(Color.FromArgb(180, 127, 119, 221)), 1.8);

    // Pre-allocated static brushes for zero GC pressure per frame
    private static readonly IBrush s_activeCoreDark = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly IBrush s_normalCoreDark = new SolidColorBrush(Color.FromRgb(170, 185, 205));
    private static readonly IBrush[] s_activeTrailDark =
    [
        new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
        new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
        new SolidColorBrush(Color.FromArgb(120, 200, 210, 255)),
        new SolidColorBrush(Color.FromArgb(70, 142, 133, 243)),
        new SolidColorBrush(Color.FromArgb(25, 142, 133, 243))
    ];
    private static readonly IBrush[] s_normalTrailDark =
    [
        new SolidColorBrush(Color.FromArgb(160, 170, 185, 205)),
        new SolidColorBrush(Color.FromArgb(110, 150, 165, 190)),
        new SolidColorBrush(Color.FromArgb(70, 130, 145, 170)),
        new SolidColorBrush(Color.FromArgb(40, 110, 125, 150)),
        new SolidColorBrush(Color.FromArgb(18, 95, 110, 135))
    ];

    private static readonly IBrush s_activeCoreLight = new SolidColorBrush(Color.FromRgb(17, 24, 39));
    private static readonly IBrush s_normalCoreLight = new SolidColorBrush(Color.FromRgb(75, 85, 99));
    private static readonly IBrush[] s_activeTrailLight =
    [
        new SolidColorBrush(Color.FromArgb(240, 31, 41, 55)),
        new SolidColorBrush(Color.FromArgb(180, 55, 65, 81)),
        new SolidColorBrush(Color.FromArgb(120, 107, 114, 128)),
        new SolidColorBrush(Color.FromArgb(70, 127, 119, 221)),
        new SolidColorBrush(Color.FromArgb(25, 127, 119, 221))
    ];
    private static readonly IBrush[] s_normalTrailLight =
    [
        new SolidColorBrush(Color.FromArgb(160, 75, 85, 99)),
        new SolidColorBrush(Color.FromArgb(110, 107, 114, 128)),
        new SolidColorBrush(Color.FromArgb(70, 156, 163, 175)),
        new SolidColorBrush(Color.FromArgb(40, 180, 185, 195)),
        new SolidColorBrush(Color.FromArgb(18, 209, 213, 219))
    ];

    private static readonly double[] s_trailRadii = [2.5, 2.1, 1.7, 1.3, 0.9];
    private static readonly double[] s_trailOffsets = [0.0, 3.5, 7.0, 10.5, 14.0];

    // Arc-length parameterization for constant speed on lines and cubic Bézier curves
    private sealed class PathTable
    {
        public Point[] Pts { get; }
        public double[] Distances { get; }
        public double TotalLength { get; }

        private PathTable(Point[] pts, double[] dists, double total)
        {
            Pts = pts;
            Distances = dists;
            TotalLength = total;
        }

        public static PathTable SampleLine(Point start, Point end, int samples = 16)
        {
            var pts = new Point[samples + 1];
            var dists = new double[samples + 1];
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double total = Math.Sqrt(dx * dx + dy * dy);

            for (int i = 0; i <= samples; i++)
            {
                double t = (double)i / samples;
                pts[i] = new Point(start.X + dx * t, start.Y + dy * t);
                dists[i] = total * t;
            }
            return new PathTable(pts, dists, total);
        }

        public static PathTable SampleCubicBezier(Point p0, Point p1, Point p2, Point p3, int samples = 64)
        {
            var pts = new Point[samples + 1];
            var dists = new double[samples + 1];
            pts[0] = p0;
            dists[0] = 0.0;
            double total = 0.0;

            for (int i = 1; i <= samples; i++)
            {
                double t = (double)i / samples;
                double u = 1.0 - t;
                double x = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
                double y = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;
                pts[i] = new Point(x, y);

                double segDx = x - pts[i - 1].X;
                double segDy = y - pts[i - 1].Y;
                total += Math.Sqrt(segDx * segDx + segDy * segDy);
                dists[i] = total;
            }
            return new PathTable(pts, dists, total);
        }

        public Point GetPointAtDistance(double distance)
        {
            if (TotalLength <= 0.0001) return Pts[0];
            double d = distance % TotalLength;
            if (d < 0) d += TotalLength;

            int idx = Array.BinarySearch(Distances, d);
            if (idx >= 0) return Pts[idx];

            int next = ~idx;
            if (next <= 0) return Pts[0];
            if (next >= Distances.Length) return Pts[^1];

            int prev = next - 1;
            double span = Distances[next] - Distances[prev];
            if (span <= 0.0001) return Pts[prev];

            double ratio = (d - Distances[prev]) / span;
            return new Point(
                Pts[prev].X + (Pts[next].X - Pts[prev].X) * ratio,
                Pts[prev].Y + (Pts[next].Y - Pts[prev].Y) * ratio);
        }
    }

    // Geometry caches
    private readonly StreamGeometry _geomPath1 = new();
    private readonly StreamGeometry _geomPath2 = new();
    private readonly StreamGeometry _geomPathTop = new();
    private readonly StreamGeometry _geomPathMid = new();
    private readonly StreamGeometry _geomPathBot = new();

    // Arc-length tables
    private readonly PathTable _tablePath1 = PathTable.SampleLine(new Point(220, 175), new Point(290, 175));
    private readonly PathTable _tablePath2 = PathTable.SampleLine(new Point(460, 175), new Point(530, 175));
    private readonly PathTable _tablePathTop = PathTable.SampleCubicBezier(new Point(700, 175), new Point(775, 175), new Point(815, 75), new Point(890, 75));
    private readonly PathTable _tablePathMid = PathTable.SampleLine(new Point(700, 175), new Point(890, 175));
    private readonly PathTable _tablePathBot = PathTable.SampleCubicBezier(new Point(700, 175), new Point(775, 175), new Point(815, 275), new Point(890, 275));

    private TopLevel? _topLevel;
    private Window? _window;
    private bool _isLooping;
    private double _lastRenderTime;
    private double _revealStartTime = -10.0;
    private int _lastActiveIndex = -1;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    static PipelineDrawingCanvas()
    {
        AffectsRender<PipelineDrawingCanvas>(ActiveIndexProperty, IsConvertingProperty);
    }

    public PipelineDrawingCanvas()
    {
        ClipToBounds = false;
        BuildGeometries();
    }

    private void BuildGeometries()
    {
        // Path 1: M 220 175 L 290 175
        using (var ctx = _geomPath1.Open())
        {
            ctx.BeginFigure(new Point(220, 175), false);
            ctx.LineTo(new Point(290, 175));
            ctx.EndFigure(false);
        }

        // Path 2: M 460 175 L 530 175
        using (var ctx = _geomPath2.Open())
        {
            ctx.BeginFigure(new Point(460, 175), false);
            ctx.LineTo(new Point(530, 175));
            ctx.EndFigure(false);
        }

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

    private bool ShouldAnimate =>
        IsEffectivelyVisible &&
        Motion.Enabled &&
        (_window == null || _window.IsActive);

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

        Motion.Changed += OnMotionChanged;

        CheckLoopState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window != null)
        {
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
        }

        Motion.Changed -= OnMotionChanged;
        StopLoop();
        _topLevel = null;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            CheckLoopState();
        }
        else if (change.Property == ActiveIndexProperty)
        {
            int newIndex = change.GetNewValue<int>();
            if (newIndex != _lastActiveIndex)
            {
                _lastActiveIndex = newIndex;
                _revealStartTime = _stopwatch.Elapsed.TotalSeconds;
            }
        }
    }

    private void OnWindowActivated(object? sender, EventArgs e) => CheckLoopState();
    private void OnWindowDeactivated(object? sender, EventArgs e) => CheckLoopState();
    private void OnMotionChanged(object? sender, EventArgs e) => CheckLoopState();

    private void CheckLoopState()
    {
        if (ShouldAnimate)
        {
            StartLoop();
        }
        else
        {
            StopLoop();
            InvalidateVisual(); // Draw static wires when paused or reduced motion
        }
    }

    private int _loopId;

    private void StartLoop()
    {
        if (_isLooping || _topLevel == null)
            return;

        _isLooping = true;
        int currentId = ++_loopId;
        _topLevel.RequestAnimationFrame(t => OnFrame(t, currentId));
    }

    private void StopLoop()
    {
        _isLooping = false;
        _loopId++;
    }

    private void OnFrame(TimeSpan time, int id)
    {
        if (!_isLooping || id != _loopId || _topLevel == null)
            return;

        if (!ShouldAnimate)
        {
            _isLooping = false;
            InvalidateVisual();
            return;
        }

        // Throttle to 30 FPS when idle (~29ms), ramp up to 60 FPS when actively converting (~12ms) with headroom
        double now = _stopwatch.Elapsed.TotalSeconds;
        double minFrameInterval = IsConverting ? 0.012 : 0.029;

        if (now - _lastRenderTime >= minFrameInterval)
        {
            _lastRenderTime = now;
            InvalidateVisual();
        }

        if (_isLooping && id == _loopId && _topLevel != null)
        {
            _topLevel.RequestAnimationFrame(t => OnFrame(t, id));
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        bool isDark = ActualThemeVariant == ThemeVariant.Dark;
        var dashPen = isDark ? s_dashPenDark : s_dashPenLight;
        var activeBranchPen = isDark ? s_activeBranchPenDark : s_activeBranchPenLight;

        // Draw static connecting paths
        context.DrawGeometry(null, dashPen, _geomPath1);
        context.DrawGeometry(null, dashPen, _geomPath2);
        context.DrawGeometry(null, ActiveIndex == 0 ? activeBranchPen : dashPen, _geomPathTop);
        context.DrawGeometry(null, ActiveIndex == 1 ? activeBranchPen : dashPen, _geomPathMid);
        context.DrawGeometry(null, ActiveIndex == 2 ? activeBranchPen : dashPen, _geomPathBot);

        // Under reduced motion or when paused, draw static wires without particle animation
        if (!Motion.Enabled)
            return;

        double elapsed = _stopwatch.Elapsed.TotalSeconds;

        // Visuals wire to real state: converting = 95 px/s and 4 particles; idle = 45 px/s and 2 particles
        double speedPxPerSec = IsConverting ? 95.0 : 45.0;
        int particleCount = IsConverting ? 4 : 2;

        var coreActive = isDark ? s_activeCoreDark : s_activeCoreLight;
        var coreNormal = isDark ? s_normalCoreDark : s_normalCoreLight;
        var trailActive = isDark ? s_activeTrailDark : s_activeTrailLight;
        var trailNormal = isDark ? s_normalTrailDark : s_normalTrailLight;

        // 1. Draw particles along straight input paths (Path 1 and Path 2)
        DrawPathParticles(context, _tablePath1, elapsed, speedPxPerSec, particleCount, coreActive, trailActive);
        DrawPathParticles(context, _tablePath2, elapsed, speedPxPerSec, particleCount, coreActive, trailActive);

        // 2. Draw branch paths with active selection highlight
        DrawPathParticles(context, _tablePathTop, elapsed, speedPxPerSec, particleCount,
            ActiveIndex == 0 ? coreActive : coreNormal,
            ActiveIndex == 0 ? trailActive : trailNormal);

        DrawPathParticles(context, _tablePathMid, elapsed, speedPxPerSec, particleCount,
            ActiveIndex == 1 ? coreActive : coreNormal,
            ActiveIndex == 1 ? trailActive : trailNormal);

        DrawPathParticles(context, _tablePathBot, elapsed, speedPxPerSec, particleCount,
            ActiveIndex == 2 ? coreActive : coreNormal,
            ActiveIndex == 2 ? trailActive : trailNormal);

        // 3. Orchestrated moment: sweep highlight pulse when active category changes (< 0.35s)
        double revealAge = elapsed - _revealStartTime;
        if (revealAge >= 0.0 && revealAge < 0.35)
        {
            double sweepProgress = revealAge / 0.35;
            PathTable activeTable = ActiveIndex switch
            {
                0 => _tablePathTop,
                2 => _tablePathBot,
                _ => _tablePathMid
            };
            double pulseDist = activeTable.TotalLength * sweepProgress;
            DrawComet(context, activeTable, pulseDist, coreActive, trailActive);
        }
    }

    private static void DrawPathParticles(DrawingContext context, PathTable path, double elapsed,
        double speedPxPerSec, int count, IBrush coreBrush, IBrush[] trailBrushes)
    {
        double spacing = path.TotalLength / count;
        double baseDist = elapsed * speedPxPerSec;

        for (int i = 0; i < count; i++)
        {
            double dist = baseDist + i * spacing;
            DrawComet(context, path, dist, coreBrush, trailBrushes);
        }
    }

    private static void DrawComet(DrawingContext context, PathTable path, double headDist, IBrush coreBrush, IBrush[] trailBrushes)
    {
        double modHead = headDist % path.TotalLength;
        if (modHead < 0) modHead += path.TotalLength;

        // Draw tail points backwards from head, skipping any that wrap behind the line entrance
        for (int i = trailBrushes.Length - 1; i >= 0; i--)
        {
            double tailDist = modHead - s_trailOffsets[i];
            if (tailDist < 0)
                continue;

            Point pt = path.GetPointAtDistance(tailDist);
            double r = s_trailRadii[i];
            context.DrawEllipse(trailBrushes[i], null, pt, r, r);
        }

        // Bright solid core at head
        Point head = path.GetPointAtDistance(modHead);
        context.DrawEllipse(coreBrush, null, head, 1.8, 1.8);
    }
}
