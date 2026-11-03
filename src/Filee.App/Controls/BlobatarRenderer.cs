using System;
using Avalonia;
using Avalonia.Media;
using Filee.App.Services;

namespace Filee.App.Controls;

/// <summary>The 5 trait tones (kept exactly as before).</summary>
public enum BlobatarTone
{
    Idle,          // 0.71  -> #8471E2
    Alert,         // 0.49  -> #A499F7
    Anticipation,  // 0.10  -> #CECAFF
    Busy,          // 0.28  -> #D6D5E9
    Celebration    // 0.865 -> #D1CEFF
}

/// <summary>
/// State of one Blobatar. Public API unchanged: it is now a facade over BlobatarPhysics (springs, expressions,
/// saccades) and CircadianLook (palette / key light). Update() returns true only for TRANSIENT motion; ambient
/// motion (breathing, heartbeat, saccades) advances but never keeps a frame loop alive on its own.
/// </summary>
public sealed class BlobatarState
{
    private const double SubStep = 1.0 / 120.0;
    private const float SubStepF = 1f / 120f;

    private readonly BlobatarLayout _layout = new() { BodyPoints = BlobatarLayout.DefaultBodyPoints };
    private readonly BlobatarPhysics _physics;
    private readonly CircadianLook _look = new();
    private BlobatarTone _tone = BlobatarTone.Idle;
    private System.Numerics.Vector2? _pointer;
    private float _deadzone = 4.5f, _sensitivity = 50f, _hover;
    private bool _dragOver, _audio;
    private double _acc;

    private BlobatarBrushCache? _cache;
    internal BlobatarBrushCache Cache => _cache ??= new();

    public BlobatarFrame Frame => _physics.Frame;
    public CircadianLookFrame Look => _look.Current;

    public BlobatarState()
    {
        _physics = new BlobatarPhysics(_layout);
        _look.Update(0f, AppHost.TryGet<CircadianService>(), _tone);
        _physics.Compose();
    }

    /// <summary>Plays Blobatar's sound cues (main-window mascot only; the donut's mascot stays silent).</summary>
    public bool AudioEnabled
    {
        get => _audio;
        set
        {
            if (_audio == value) return;
            _audio = value;
            if (value) _physics.Raised += BlobatarAudio.Shared.Handle;
            else _physics.Raised -= BlobatarAudio.Shared.Handle;
        }
    }

    // ----- Legacy surface (values in viewBox units, same meaning as before) -----

    public double LeftGazeX => Frame.Left.Gaze.X * (double)_layout.EyeMaxL.X;
    public double LeftGazeY => Frame.Left.Gaze.Y * (double)_layout.EyeMaxL.Y;
    public double RightGazeX => Frame.Right.Gaze.X * (double)_layout.EyeMaxR.X;
    public double RightGazeY => Frame.Right.Gaze.Y * (double)_layout.EyeMaxR.Y;
    public double GazeX => (LeftGazeX + RightGazeX) / 2.0;
    public double GazeY => (LeftGazeY + RightGazeY) / 2.0;

    public double LeftEyeTiltDeg => Math.Clamp(LeftGazeX / 4.8, -1.0, 1.0) * 8.0 - Math.Clamp(LeftGazeY / 4.0, -1.0, 1.0) * 3.5 * Math.Sign(LeftGazeX);
    public double RightEyeTiltDeg => Math.Clamp(RightGazeX / 4.6, -1.0, 1.0) * 8.0 - Math.Clamp(RightGazeY / 3.8, -1.0, 1.0) * 3.5 * Math.Sign(RightGazeX);
    public double LeftForeshortenX => LeftGazeX > 0 ? (1.0 - Math.Clamp(LeftGazeX / 4.8, 0.0, 1.0) * 0.18) : (1.0 + Math.Clamp(-LeftGazeX / 4.8, 0.0, 1.0) * 0.04);
    public double RightForeshortenX => RightGazeX < 0 ? (1.0 - Math.Clamp(-RightGazeX / 4.6, 0.0, 1.0) * 0.18) : (1.0 + Math.Clamp(RightGazeX / 4.6, 0.0, 1.0) * 0.04);

    public double LeftBlinkScaleY => 1.0 - Frame.Left.Lid * 0.94;
    public double RightBlinkScaleY => 1.0 - Frame.Right.Lid * 0.94;
    public double BlinkScaleY => Math.Min(LeftBlinkScaleY, RightBlinkScaleY);

    public double HappyCurve => Frame.Smile;
    public double ScaleX => Frame.Sx;
    public double ScaleY => Frame.Sy;
    public double FloatY => Frame.Hop;
    public double TiltAngle => Frame.LeanDeg;
    public Color CurrentColor => _look.Current.Mid;

    // ----- Commands -----

    public void SetTone(BlobatarTone tone)
    {
        if (tone == _tone) return;
        _tone = tone;
        if (tone == BlobatarTone.Celebration)
        {
            _physics.Celebrate();
            _physics.Blink(3);
        }
    }

    public void TriggerBounce(double impulse = 0.45) => _physics.Bounce((float)impulse);
    public void TriggerTiltWobble(double degrees = 8.0) => _physics.Wobble((float)degrees);
    public void TriggerBlink(int count = 1) => _physics.Blink(count);
    public void TriggerWink(bool leftEye = true) => _physics.Wink(leftEye);
    public void SetHappy(double amount = 1.0, double duration = 1.2) { if (amount > 0) _physics.Happy((float)duration); }

    public void Press() => _physics.Press();
    public void Release() => _physics.Release();
    public void Drop() => _physics.Drop();
    public void Startle(double stimulusDx) => _physics.Startle((float)stimulusDx);
    public void Proud() => _physics.Proud();
    public void Celebrate() => _physics.Celebrate();
    public void Poke() => _physics.Poke();
    public void SetThinking(bool thinking, double load = 0) { _physics.Thinking = thinking; _physics.Load = (float)load; }
    public void SetDragOver(bool over) => _dragOver = over;

    /// <summary>0..1 proximity boost from outside (the donut's hovered slice): the body leans and stretches as if the cursor were close.</summary>
    public void SetHoverEnergy(double amount) => _hover = (float)Math.Clamp(amount, 0.0, 1.0);

    /// <summary>Directs both eyes toward a cursor in the mascot's 100x100 viewBox space (null = no cursor).</summary>
    public void LookAtCursor(Point? cursorInViewBox, double deadzone = 4.5, double sensitivity = 50.0)
    {
        if (cursorInViewBox is not { } c) { _pointer = null; return; }
        _pointer = new System.Numerics.Vector2((float)c.X, (float)c.Y);
        _deadzone = (float)deadzone;
        _sensitivity = (float)sensitivity;
    }

    /// <summary>Look at a target relative to the mascot's visual centre (radial slices, directional offsets).</summary>
    public void LookAt(Point? cursorRelativeToCenter, double sensitivity = 130.0)
    {
        if (cursorRelativeToCenter is not { } p || (Math.Abs(p.X) < 0.001 && Math.Abs(p.Y) < 0.001))
        {
            LookAtCursor(null);
            return;
        }
        LookAtCursor(new Point(50.0 + p.X * 0.45, 52.5 + p.Y * 0.45), deadzone: 3.5, sensitivity: sensitivity * 0.45);
    }

    /// <summary>Advances the simulation. Returns true while transient motion (or a palette cross-fade) needs frames.</summary>
    public bool Update(double dt, bool motionEnabled = true)
    {
        dt = Math.Clamp(dt, 0.0, 0.25);
        _physics.ReduceMotion = !motionEnabled;

        _look.Update((float)dt, AppHost.TryGet<CircadianService>(), _tone);
        var look = _look.Current;

        var h = DateTime.Now.TimeOfDay.TotalHours;
        var clockSleepy = !look.Active ? 0f
            : h >= 12 ? BlobMath.Sm(21f, 23.5f, (float)h) : 1f - BlobMath.Sm(4.5f, 6f, (float)h);

        var inp = new BlobatarInput
        {
            Pointer = _pointer, DragOver = _dragOver, Hover = _hover, ClockSleepy = clockSleepy,
            Intensity = look.Intensity, Deadzone = _deadzone, Sensitivity = _sensitivity,
        };

        _acc += dt;
        for (var n = 0; _acc >= SubStep && n < 40; n++)
        {
            _physics.Step(SubStepF, inp);
            _acc -= SubStep;
        }
        _physics.Compose();

        if (_audio) BlobatarAudio.Shared.SetTimeOfDay(look.Wm, look.Wa, look.Wn);
        return _physics.Animating || _look.Transitioning;
    }
}

/// <summary>Brushes that only change with the circadian look; rebuilt when <see cref="CircadianLookFrame.CacheKey"/> changes.</summary>
internal sealed class BlobatarBrushCache
{
    private int _key = int.MinValue;
    public IBrush Body = Brushes.Transparent;
    public IBrush Spec = Brushes.Transparent;
    public IBrush GlintPrimary = Brushes.White;
    public IBrush GlintAmbient = Brushes.Transparent;
    public IPen RimPen = null!;
    public IPen GlowPen = null!;

    private static Color A(Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255.0, 0, 255), c.R, c.G, c.B);

    public void Ensure(CircadianLookFrame look)
    {
        if (look.CacheKey == _key) return;
        _key = look.CacheKey;

        double lx = look.Light.X, ly = look.Light.Y;
        RelativePoint toLight = new(0.5 + 0.5 * lx, 0.5 + 0.5 * ly, RelativeUnit.Relative);
        RelativePoint away = new(0.5 - 0.5 * lx, 0.5 - 0.5 * ly, RelativeUnit.Relative);

        Body = new LinearGradientBrush
        {
            StartPoint = toLight, EndPoint = away,
            GradientStops = { new GradientStop(look.Hi, 0.0), new GradientStop(look.Mid, 0.45), new GradientStop(look.Lo, 1.0) },
        };

        // Forehead specular: white by day, neon rim colour at night.
        var tint = Color.FromRgb(
            (byte)(255 + (look.Rim.R - 255) * look.Wn), (byte)(255 + (look.Rim.G - 255) * look.Wn), (byte)(255 + (look.Rim.B - 255) * look.Wn));
        var specAlpha = BlobMath.Lerp(0.35f * look.Intensity, 0.18f, look.Wn);
        Spec = new RadialGradientBrush { GradientStops = { new GradientStop(A(tint, specAlpha), 0.0), new GradientStop(A(tint, 0), 1.0) } };

        // Rim light along the key light, plus a faint bounce rim on the far side.
        var ga = Math.Clamp(look.Gain, 0.5, 1.1);
        var rim = new LinearGradientBrush
        {
            StartPoint = toLight, EndPoint = away,
            GradientStops =
            {
                new GradientStop(A(look.Rim, 0.9 * ga), 0.0), new GradientStop(A(look.Rim, 0), 0.35),
                new GradientStop(A(look.Sky, 0.18), 0.8), new GradientStop(A(look.Sky, 0.18), 1.0),
            },
        };
        RimPen = new Pen(rim, 3.0);
        GlowPen = new Pen(new LinearGradientBrush
        {
            StartPoint = toLight, EndPoint = away,
            GradientStops = { new GradientStop(A(look.Rim, 0.08 * ga), 0.0), new GradientStop(A(look.Rim, 0), 0.5) },
        }, 6.0);

        GlintPrimary = new SolidColorBrush(A(look.Rim, 0.55 + 0.45 * look.Intensity));
        GlintAmbient = new SolidColorBrush(A(look.Sky, 0.22));
    }
}

/// <summary>Hardware-accelerated renderer for the Blobatar owl. Reads state.Frame / state.Look only.</summary>
public static class BlobatarRenderer
{
    private static Geometry? _bodyGeometry;
    private static Geometry BodyGeometry => _bodyGeometry ??= Geometry.Parse(
        "M85.02 51.18C85.02 71.96 72.08 84.14 50.01 84.14C27.94 84.14 15.01 71.96 15.01 51.18C15.01 30.39 27.94 18.21 50.01 18.21C72.08 18.21 85.02 30.39 85.02 51.18Z");

    private static Geometry? _leftEyeGeometry;
    private static Geometry LeftEyeGeometry => _leftEyeGeometry ??= Geometry.Parse(
        "M45.05 44.46C45.7 49.75 45.34 50.71 42.56 51.05C39.79 51.39 39.2 50.54 38.56 45.25C37.91 39.96 38.27 39 41.05 38.66C43.82 38.32 44.41 39.17 45.05 44.46Z");

    private static Geometry? _rightEyeGeometry;
    private static Geometry RightEyeGeometry => _rightEyeGeometry ??= Geometry.Parse(
        "M65.89 45.33C66.5 49.15 66.22 49.86 63.98 50.21C61.74 50.56 61.26 49.97 60.66 46.16C60.06 42.35 60.33 41.64 62.57 41.28C64.81 40.93 65.29 41.52 65.89 45.33Z");

    // The body path's 12 Bézier control points: N0 h h N1 h h N2 h h N3 h h (right, bottom, left, top).
    private static readonly Point[] BodyRest =
    [
        new(85.02, 51.18), new(85.02, 71.96), new(72.08, 84.14), new(50.01, 84.14),
        new(27.94, 84.14), new(15.01, 71.96), new(15.01, 51.18), new(15.01, 30.39),
        new(27.94, 18.21), new(50.01, 18.21), new(72.08, 18.21), new(85.02, 30.39),
    ];

    internal static System.Numerics.Vector2[] BodyRestPoints => BlobatarLayout.DefaultBodyPoints;

    public static readonly Color EyeColor = Color.FromRgb(0x0F, 0x0E, 0x18);
    private static IBrush? _eyeBrush;
    private static IBrush EyeBrush => _eyeBrush ??= new SolidColorBrush(EyeColor);

    public static Color ColorForTone(BlobatarTone tone) => tone switch
    {
        BlobatarTone.Idle => Color.FromRgb(0x84, 0x71, 0xE2),
        BlobatarTone.Alert => Color.FromRgb(0xA4, 0x99, 0xF7),
        BlobatarTone.Anticipation => Color.FromRgb(0xCE, 0xCA, 0xFF),
        BlobatarTone.Busy => Color.FromRgb(0xD6, 0xD5, 0xE9),
        BlobatarTone.Celebration => Color.FromRgb(0xD1, 0xCE, 0xFF),
        _ => Color.FromRgb(0x84, 0x71, 0xE2)
    };

    public static void Render(DrawingContext context, Rect bounds, BlobatarState state)
    {
        var size = Math.Min(bounds.Width, bounds.Height);
        var scale = size / 70.0;
        if (scale <= 0.001)
            return;

        // Chubby, squishy mochi proportions (1.14x width, 0.88x height) so mascot never stretches up as a tall egg
        var rootMatrix = Matrix.CreateTranslation(-50, -52.5) *
                         Matrix.CreateScale(scale * 1.14, scale * 0.88) *
                         Matrix.CreateTranslation(bounds.Center.X, bounds.Center.Y + 2.0);

        var f = state.Frame;
        var look = state.Look;
        var cache = state.Cache;
        cache.Ensure(look);

        using (context.PushTransform(rootMatrix))
        {
            DrawShadow(context, look, f);

            var body = BuildBody(f);
            using (context.PushTransform(f.Body.ToAvalonia()))
            {
                context.DrawGeometry(cache.Body, null, body);

                using (context.PushGeometryClip(body))
                {
                    // Forehead specular, placed toward the key light.
                    var sx = 50.0 + look.Light.X * 35.0 * 0.7;
                    var sy = 51.2 + look.Light.Y * 33.0 * 0.7;
                    context.DrawEllipse(cache.Spec, null, new Point(sx, sy), 11, 7);
                    context.DrawGeometry(null, cache.GlowPen, body);
                    context.DrawGeometry(null, cache.RimPen, body);
                }
            }

            // Eyes are drawn outside the body matrix (translation only) so gaze stays exact while the body breathes.
            DrawEye(context, LeftEyeGeometry, f.Left, new Point(41.8, 44.8), 4.8, 4.0, state.LeftEyeTiltDeg, state.LeftForeshortenX, f, state.HappyCurve, look, cache, 1.0);
            DrawEye(context, RightEyeGeometry, f.Right, new Point(63.3, 45.7), 4.6, 3.8, state.RightEyeTiltDeg, state.RightForeshortenX, f, state.HappyCurve, look, cache, 0.88);
        }
    }

    private static void DrawShadow(DrawingContext context, CircadianLookFrame look, BlobatarFrame f)
    {
        var sh = CircadianLook.Shadow(look, f.Sx, f.Sy, f.Hop);
        var at = new Point(50.0 + sh.Dx, 84.5);
        if (sh.Alpha > 0.003)
        {
            var brush = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)Math.Clamp(sh.Alpha * 255f, 0, 255), sh.Color.R, sh.Color.G, sh.Color.B), 0.0),
                    new GradientStop(Color.FromArgb(0, sh.Color.R, sh.Color.G, sh.Color.B), 1.0),
                },
            };
            context.DrawEllipse(brush, null, at, sh.Rx, sh.Ry * 1.3);
        }
        if (sh.CoreAlpha > 0.01)
        {
            var core = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)Math.Clamp(sh.CoreAlpha * 255f, 0, 255), 0, 0, 0), 0.0),
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0),
                },
            };
            context.DrawEllipse(core, null, at, sh.Rx * 0.55, sh.Ry * 0.8);
        }
    }

    /// <summary>The static path, or a rebuilt one when the contour wobbles or a tuft is raised.</summary>
    private static Geometry BuildBody(BlobatarFrame f)
    {
        var contour = f.Contour;
        if (contour is null && Math.Abs(f.TuftL) < 0.01f && Math.Abs(f.TuftR) < 0.01f)
            return BodyGeometry;

        var p = new Point[BodyRest.Length];
        for (var i = 0; i < p.Length; i++)
        {
            var o = contour is not null && i < contour.Length ? contour[i] : default;
            p[i] = new Point(BodyRest[i].X + o.X, BodyRest[i].Y + o.Y);
        }
        // Tufts: raise the shoulder handles on each side of the head (negative = up).
        p[8] = new Point(p[8].X, p[8].Y + f.TuftL);
        p[7] = new Point(p[7].X, p[7].Y + f.TuftL * 0.4);
        p[10] = new Point(p[10].X, p[10].Y + f.TuftR);
        p[11] = new Point(p[11].X, p[11].Y + f.TuftR * 0.4);

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(p[0], true);
            c.CubicBezierTo(p[1], p[2], p[3]);
            c.CubicBezierTo(p[4], p[5], p[6]);
            c.CubicBezierTo(p[7], p[8], p[9]);
            c.CubicBezierTo(p[10], p[11], p[0]);
            c.EndFigure(true);
        }
        return g;
    }

    private static void DrawEye(DrawingContext context, Geometry geo, BlobatarEye e, Point rest, double maxX, double maxY,
        double tiltDeg, double foreshortenX, BlobatarFrame f, double happy, CircadianLookFrame look,
        BlobatarBrushCache cache, double glintScale)
    {
        double nx = e.Gaze.X, ny = e.Gaze.Y;
        var pitch = ny < 0 ? 1.0 - (-ny) * 0.08 : 1.0 - ny * 0.05;
        var blink = Math.Max(0.06, (1.0 - e.Lid * 0.94) * (1.0 - happy * 0.65));

        // The eyes are solid "pupils": dilation = eye size (rho .57 is the resting size).
        var dil = 1.0 + (e.PupilRho - 0.57) * 0.35;
        var deformX = 1.0 + (f.Sx - 1.0) * 0.6;       // the eyes squash with the body, a little less
        var deformY = 1.0 + (f.Sy - 1.0) * 0.6;
        var sx = foreshortenX * e.Scale * dil * deformX;
        var sy = Math.Max(0.02, blink * pitch * (1.0 - e.Squint * 0.9) * e.Scale * dil * deformY);
        var rot = tiltDeg * Math.PI / 180.0 + e.Roll;

        var px = e.Center.X + nx * maxX;
        var py = e.Center.Y + ny * maxY - e.Brow * 0.35;      // "brow": lift the eye

        var m = Matrix.CreateTranslation(-rest.X, -rest.Y) * Matrix.CreateScale(sx, sy) *
                Matrix.CreateRotation(rot) * Matrix.CreateTranslation(px, py);
        using (context.PushTransform(m))
            context.DrawGeometry(EyeBrush, null, geo);

        if (blink <= 0.40)
            return;

        // Primary catchlight tracks the key light (sun by day, moon / neon at night) and slides opposite the gaze.
        double lx = look.Light.X, ly = look.Light.Y;
        var r1 = (happy > 0.4 ? 1.1 : 0.85) * glintScale * dil;
        context.DrawEllipse(cache.GlintPrimary, null, new Point(px + lx * 2.3 * glintScale - nx * 0.85, py + ly * 2.3 * glintScale - ny * 0.55), r1, r1);

        // Secondary: soft ambient glint opposite the key light, rotated 20 degrees.
        const double Rot = 20.0 * Math.PI / 180.0;
        var ax = -(lx * Math.Cos(Rot) - ly * Math.Sin(Rot));
        var ay = -(lx * Math.Sin(Rot) + ly * Math.Cos(Rot));
        var r2 = 0.45 * glintScale * dil;
        context.DrawEllipse(cache.GlintAmbient, null, new Point(px + ax * 1.3 - nx * 0.4, py + ay * 1.3 - ny * 0.3), r2, r2);
    }
}
