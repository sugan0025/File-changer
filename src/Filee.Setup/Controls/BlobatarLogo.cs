// Interactive Blobatar mascot for Filee Setup: tracks the installation loading bar,
// reacts to mouse interaction, and celebrates completion with bouncy jelly physics.

using System;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Filee.Setup.Controls;

public enum BlobatarTone
{
    Idle,
    Alert,
    Anticipation,
    Busy,
    Celebration
}

public sealed class BlobatarLogo : Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<BlobatarLogo, double>(nameof(Progress), 0);

    public static readonly StyledProperty<bool> TrackProgressProperty =
        AvaloniaProperty.Register<BlobatarLogo, bool>(nameof(TrackProgress), false);

    public static readonly StyledProperty<BlobatarTone> ToneProperty =
        AvaloniaProperty.Register<BlobatarLogo, BlobatarTone>(nameof(Tone), BlobatarTone.Idle);

    public static readonly StyledProperty<bool> AnimateProperty =
        AvaloniaProperty.Register<BlobatarLogo, bool>(nameof(Animate), true);

    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool TrackProgress { get => GetValue(TrackProgressProperty); set => SetValue(TrackProgressProperty, value); }
    public BlobatarTone Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public bool Animate { get => GetValue(AnimateProperty); set => SetValue(AnimateProperty, value); }

    // Mochi geometry constants from BlobatarLayout
    public const double FaceX = 52.55;
    public const double BodyCy = 51.18;
    private const double PlumpX = 1.08;
    private const double PlumpY = 0.92;
    private const double EyeRx = 3.7;
    private const double EyeRy = 5.9;
    private const double EyeMaxX = 4.8;
    private const double EyeMaxY = 4.0;
    private const double LeftEyeRestX = 41.8;
    private const double RightEyeRestX = 63.3;
    private const double EyeRestY = 44.8;

    private static readonly Point[] MochiControlPoints = BuildMochiPoints();

    private static Point[] BuildMochiPoints()
    {
        const double x0 = FaceX, w = 35.0, top = 18.21, bottom = 84.14, belly = 56.0;
        var lowY = belly + 0.64 * (bottom - belly);
        var upY = belly - 0.56 * (belly - top);
        return
        [
            new(x0 + w, belly), new(x0 + w, lowY), new(x0 + w * 0.70, bottom), new(x0, bottom),
            new(x0 - w * 0.70, bottom), new(x0 - w, lowY), new(x0 - w, belly), new(x0 - w, upY),
            new(x0 - w * 0.56, top), new(x0, top), new(x0 + w * 0.56, top), new(x0 + w, upY),
        ];
    }

    private static Geometry? _cachedBodyGeometry;
    private static Geometry BodyGeometry => _cachedBodyGeometry ??= BuildBodyPath();

    private static Geometry BuildBodyPath()
    {
        var p = MochiControlPoints;
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(p[0], true);
        c.CubicBezierTo(p[1], p[2], p[3]);
        c.CubicBezierTo(p[4], p[5], p[6]);
        c.CubicBezierTo(p[7], p[8], p[9]);
        c.CubicBezierTo(p[10], p[11], p[0]);
        c.EndFigure(true);
        return g;
    }

    private static readonly Geometry EyeGeometry = new EllipseGeometry(new Rect(-EyeRx, -EyeRy, EyeRx * 2, EyeRy * 2));
    private static readonly IBrush EyeBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x0F, 0x0E, 0x18));
    private static readonly IBrush BlushBrush = new ImmutableSolidColorBrush(Color.FromArgb(70, 0xFF, 0x8C, 0xAA));
    private static readonly IPen OutlinePen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(90, 0x3C, 0x34, 0x89)), 1.1);
    private static readonly IPen DarkEyeNeonPen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(140, 0x8C, 0x7C, 0xFF)), 0.6);

    // Spring simulation struct
    private struct Spring(float k, float zeta)
    {
        public float X, V, Target;
        public readonly float K = k, D = 2f * zeta * MathF.Sqrt(k);

        public void Step(float dt)
        {
            V += (-K * (X - Target) - D * V) * dt;
            X += V * dt;
        }

        public void Kick(float dv) => V += dv;
    }

    private readonly FrameLoop _loop;
    private TopLevel? _topLevel;
    private double _elapsedSeconds;
    private bool _isHovered;
    private bool _isPressed;
    private bool _celebrated;

    // Physics springs
    private Spring _gazeX = new(240f, 0.82f);
    private Spring _gazeY = new(240f, 0.82f);
    private Spring _leanDeg = new(180f, 0.65f);
    private Spring _squish = new(260f, 0.38f);
    private Spring _hop = new(160f, 0.55f);
    private Spring _smile = new(280f, 0.80f);
    private Spring _blink = new(320f, 0.90f);

    private double _blinkTimer = 2.5;
    private bool _winking;

    static BlobatarLogo()
    {
        AffectsRender<BlobatarLogo>(ProgressProperty, TrackProgressProperty, ToneProperty, AnimateProperty);
    }

    public BlobatarLogo()
    {
        FlowDirection = FlowDirection.LeftToRight;
        ClipToBounds = false;

        _loop = new FrameLoop(this, elapsed =>
        {
            if (!Animate || !SetupMotion.Enabled)
                return false;

            var dt = Math.Clamp(elapsed.TotalSeconds - _elapsedSeconds, 0.001, 0.05);
            _elapsedSeconds = elapsed.TotalSeconds;

            UpdateSimulation((float)dt);
            InvalidateVisual();
            return true;
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.PointerMoved += OnWindowPointerMoved;
            _topLevel.PointerExited += OnWindowPointerExited;
        }
        _loop.Kick();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
        {
            _topLevel.PointerMoved -= OnWindowPointerMoved;
            _topLevel.PointerExited -= OnWindowPointerExited;
            _topLevel = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ProgressProperty)
        {
            if (Progress >= 99.5 && !_celebrated)
            {
                _celebrated = true;
                Celebrate();
            }
            else if (Progress < 99.5)
            {
                _celebrated = false;
            }
            _loop.Kick();
        }
        else if (change.Property == ToneProperty)
        {
            if (Tone == BlobatarTone.Celebration)
                Celebrate();
            _loop.Kick();
        }
    }

    public void Celebrate()
    {
        _hop.Kick(36f);
        _squish.Kick(-0.25f);
        _smile.Target = 1.0f;
        _leanDeg.Kick((Random.Shared.NextDouble() > 0.5 ? 1f : -1f) * 12f);
        _loop.Kick();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _isHovered = true;
        _squish.Kick(0.18f);
        _leanDeg.Kick(6.0f);
        _loop.Kick();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isHovered = false;
        _isPressed = false;
        _loop.Kick();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _isPressed = true;
        _squish.Target = 0.22f; // squash down into jelly
        _winking = true;
        _blink.Target = 1.0f;
        _loop.Kick();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isPressed)
        {
            _isPressed = false;
            _squish.Target = 0f;
            _squish.Kick(-0.35f); // jelly pop stretch
            _winking = false;
            _blink.Target = 0f;
            _smile.Kick(0.5f);
        }
        _loop.Kick();
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (TrackProgress && !_isHovered)
            return; // Progress bar tracking takes precedence during installation unless hovered directly

        var posInControl = e.GetPosition(this);
        var bounds = Bounds;
        if (bounds.Width <= 1 || bounds.Height <= 1)
            return;

        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        var dx = posInControl.X - center.X;
        var dy = posInControl.Y - center.Y;

        AimGaze(dx, dy, sensitivity: 90.0);
        _loop.Kick();
    }

    private void OnWindowPointerExited(object? sender, PointerEventArgs e)
    {
        if (TrackProgress)
            return;

        _gazeX.Target = 0f;
        _gazeY.Target = 0f;
        _leanDeg.Target = 0f;
        _loop.Kick();
    }

    private void AimGaze(double dx, double dy, double sensitivity)
    {
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 0.001)
        {
            _gazeX.Target = 0f;
            _gazeY.Target = 0f;
            _leanDeg.Target = 0f;
            return;
        }

        var ux = dx / dist;
        var uy = dy / dist;
        var gain = Math.Tanh(dist / sensitivity);

        _gazeX.Target = (float)(ux * EyeMaxX * gain);
        _gazeY.Target = (float)(uy * EyeMaxY * gain);
        _leanDeg.Target = (float)(Math.Clamp(ux * gain, -1.0, 1.0) * 5.0);
    }

    private void UpdateSimulation(float dt)
    {
        if (TrackProgress)
        {
            if (_isHovered && _topLevel is not null)
            {
                // If user hovers directly over the mascot, interact with the user
                _smile.Target = 0.6f;
            }
            else
            {
                // Track the filling tip of the progress bar below
                double dx, dy;
                if (TryGetBarFillOffset(out var fillOffset))
                {
                    dx = fillOffset.X;
                    dy = fillOffset.Y;
                }
                else
                {
                    var fraction = Math.Clamp(Progress / 100.0, 0.0, 1.0);
                    dx = (fraction - 0.5) * 440.0;
                    dy = 190.0;
                }

                AimGaze(dx, dy, sensitivity: 130.0);

                if (_celebrated || Progress >= 99.5)
                {
                    _smile.Target = 1.0f;
                }
                else
                {
                    _smile.Target = 0.25f; // focused little smile while working
                }
            }
        }
        else if (Tone == BlobatarTone.Celebration)
        {
            _smile.Target = 1.0f;
        }

        // Autonomous blinking
        _blinkTimer -= dt;
        if (_blinkTimer <= 0)
        {
            _blinkTimer = 3.0 + Random.Shared.NextDouble() * 2.5;
            _blink.Kick(12f);
        }

        // Step all springs
        _gazeX.Step(dt);
        _gazeY.Step(dt);
        _leanDeg.Step(dt);
        _squish.Step(dt);
        _hop.Step(dt);
        _smile.Step(dt);
        _blink.Step(dt);
    }

    private bool TryGetBarFillOffset(out Point offset)
    {
        offset = default;
        if (_topLevel is null)
            return false;

        var bar = _topLevel.GetVisualDescendants().OfType<ShimmerBar>().FirstOrDefault();
        if (bar is null || bar.Bounds.Width <= 1.0)
            return false;

        var barPosInSelf = bar.TranslatePoint(new Point(0, 0), this);
        if (barPosInSelf is not { } origin)
            return false;

        var fraction = Math.Clamp(Progress / 100.0, 0.0, 1.0);
        var fillX = origin.X + bar.Bounds.Width * fraction;
        var fillY = origin.Y + bar.Bounds.Height / 2.0;

        var selfCenter = new Point(Bounds.Width / 2, Bounds.Height / 2);
        offset = new Point(fillX - selfCenter.X, fillY - selfCenter.Y);
        return true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(double.IsInfinity(availableSize.Width) ? 96 : availableSize.Width,
                            double.IsInfinity(availableSize.Height) ? 96 : availableSize.Height);
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 1.0)
            return;

        var animated = Animate && SetupMotion.Enabled;
        var scale = size / 70.0;

        // Colors based on theme variant
        var isDark = ActualThemeVariant == ThemeVariant.Dark;

        Color hiColor, midColor, loColor, rimColor, skyColor, shadowColor;
        if (isDark)
        {
            // Night obsidian look
            hiColor = Color.Parse("#5F52D6");
            midColor = Color.Parse("#372C86");
            loColor = Color.Parse("#130E38");
            rimColor = Color.Parse("#8C7CFF");
            skyColor = Color.Parse("#6EF0FF");
            shadowColor = Color.Parse("#8C7CFF");
        }
        else
        {
            // Day lilac / soft violet look
            hiColor = Color.Parse("#D3D6FF");
            midColor = Color.Parse("#9A92EE");
            loColor = Color.Parse("#6C69CF");
            rimColor = Color.Parse("#EAF4FF");
            skyColor = Color.Parse("#BFD9FF");
            shadowColor = Color.Parse("#3A3C8F");
        }

        // Dynamic transforms
        var breathing = animated ? MathF.Sin((float)_elapsedSeconds * 2.4f) * 0.015f : 0f;
        var squishVal = animated ? _squish.X : 0f;
        var hopVal = animated ? Math.Max(0f, _hop.X) : 0f;
        var leanVal = animated ? _leanDeg.X : 0f;

        var sy = 1.0 - squishVal + breathing;
        var sx = Math.Pow(sy, -0.9);

        // Root centering matrix
        var rootMatrix = Matrix.CreateTranslation(-FaceX, -BodyCy) *
                         Matrix.CreateScale(scale, scale) *
                         Matrix.CreateTranslation(bounds.Center.X, bounds.Center.Y);

        var plump = Matrix.CreateTranslation(-FaceX, -BodyCy) *
                    Matrix.CreateScale(PlumpX, PlumpY) *
                    Matrix.CreateTranslation(FaceX, BodyCy);

        // Body squash, lean, and hop
        var leanRad = leanVal * Math.PI / 180.0;
        var bodyMatrix = Matrix.CreateTranslation(-FaceX, -84.14) *
                         Matrix.CreateScale(sx, sy) *
                         new Matrix(1.0, 0.0, -Math.Tan(leanRad), 1.0, 0.0, 0.0) *
                         Matrix.CreateTranslation(FaceX, 84.14 - hopVal);

        using (context.PushTransform(rootMatrix))
        {
            // Ground contact shadow
            var shadowRx = 24.0 * sx;
            var shadowRy = 4.5 / sy;
            var shadowAlpha = Math.Clamp(0.35 * (1.0 - hopVal / 35.0), 0.0, 1.0);
            if (shadowAlpha > 0.01)
            {
                var shadowBrush = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb((byte)(shadowAlpha * 255), shadowColor.R, shadowColor.G, shadowColor.B), 0.0),
                        new GradientStop(Color.FromArgb(0, shadowColor.R, shadowColor.G, shadowColor.B), 1.0),
                    }
                };
                context.DrawEllipse(shadowBrush, null, new Point(FaceX, 84.5), shadowRx, shadowRy * 1.3);
            }

            // Body
            using (context.PushTransform(plump))
            using (context.PushTransform(bodyMatrix))
            {
                var bodyBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(hiColor, 0.0),
                        new GradientStop(midColor, 0.45),
                        new GradientStop(loColor, 1.0),
                    }
                };

                context.DrawGeometry(bodyBrush, OutlinePen, BodyGeometry);

                using (context.PushGeometryClip(BodyGeometry))
                {
                    // Forehead specular
                    var specBrush = new RadialGradientBrush
                    {
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(90, 255, 255, 255), 0.0),
                            new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0),
                        }
                    };
                    context.DrawEllipse(specBrush, null, new Point(FaceX, 36.0), 11, 7);

                    // Rosy cheeks
                    context.DrawEllipse(BlushBrush, null, new Point(39.9, 54.0), 4.4, 2.5);
                    context.DrawEllipse(BlushBrush, null, new Point(65.2, 54.0), 4.4, 2.5);

                    // Soft rim stroke
                    var rimPen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(120, rimColor.R, rimColor.G, rimColor.B)), 2.8);
                    context.DrawGeometry(null, rimPen, BodyGeometry);
                }

                // Smile
                var smileVal = animated ? Math.Clamp(_smile.X, 0f, 1f) : 0.2f;
                if (smileVal > 0.05)
                {
                    var mouthGeometry = new StreamGeometry();
                    using (var mc = mouthGeometry.Open())
                    {
                        mc.BeginFigure(new Point(-2.6, 0), false);
                        mc.QuadraticBezierTo(new Point(0, 4.0 * smileVal), new Point(2.6, 0));
                        mc.EndFigure(false);
                    }
                    var mouthColor = isDark ? rimColor : Color.FromRgb(0x0F, 0x0E, 0x18);
                    var mouthPen = new Pen(new ImmutableSolidColorBrush(mouthColor), 1.2, lineCap: PenLineCap.Round);
                    using (context.PushTransform(Matrix.CreateTranslation(FaceX, 56.8)))
                        context.DrawGeometry(null, mouthPen, mouthGeometry);
                }
            }

            // Gaze and eyes (drawn with eye offsets + head movement)
            var gx = animated ? Math.Clamp(_gazeX.X, -EyeMaxX, EyeMaxX) : 0;
            var gy = animated ? Math.Clamp(_gazeY.X, -EyeMaxY, EyeMaxY) : 0;
            var eyePen = isDark ? DarkEyeNeonPen : null;

            // Blink calculation
            var blinkAmt = animated ? Math.Clamp(Math.Abs(_blink.X), 0f, 1f) : 0f;
            var leftBlink = _winking ? 0.92 : blinkAmt;
            var rightBlink = blinkAmt;

            DrawSingleEye(context, new Point(LeftEyeRestX + gx, EyeRestY + gy - hopVal), gx, gy, leftBlink, eyePen);
            DrawSingleEye(context, new Point(RightEyeRestX + gx, EyeRestY + gy - hopVal), gx, gy, rightBlink, eyePen);
        }
    }

    private static void DrawSingleEye(DrawingContext context, Point at, double gx, double gy, double blink, IPen? eyePen)
    {
        var scaleY = Math.Max(0.06, 1.0 - blink * 0.94);
        var eyeTransform = Matrix.CreateScale(1.0, scaleY) * Matrix.CreateTranslation(at.X, at.Y);

        using (context.PushTransform(eyeTransform))
            context.DrawGeometry(EyeBrush, eyePen, EyeGeometry);

        if (blink > 0.45)
            return; // Eyelid covers glints during blink

        // Primary dynamic catchlight
        var glint1 = new Point(at.X - gx * 0.22 - 0.9, at.Y - gy * 0.22 - 1.4);
        context.DrawEllipse(Brushes.White, null, glint1, 1.35, 1.35);

        // Secondary ambient catchlight
        var glint2 = new Point(at.X - gx * 0.12 + 1.1, at.Y - gy * 0.12 + 1.2);
        var glint2Brush = new ImmutableSolidColorBrush(Color.FromArgb(180, 255, 255, 255));
        context.DrawEllipse(glint2Brush, null, glint2, 0.65, 0.65);
    }
}
