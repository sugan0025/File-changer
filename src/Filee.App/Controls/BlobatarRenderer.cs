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

    private readonly BlobatarLayout _layout = new() { BodyPoints = BlobatarRenderer.BodyRestPoints };
    private readonly BlobatarPhysics _physics;
    private readonly CircadianLook _look = new();
    private BlobatarTone _tone = BlobatarTone.Idle;
    private System.Numerics.Vector2? _pointer;
    private float _deadzone = 4.5f, _sensitivity = 50f, _hover;
    private float _dragLean, _dragStretch;
    private float _restLean, _fullness, _eyeRollDegrees, _eyeRollDuration, _eyeRollRemaining;
    private bool _dragOver, _audio;
    private bool _curious, _paused, _napping;
    private bool _companionMode;
    private bool _suppressAutonomousWinks;
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

    /// <summary>Changes the companion's tint without invoking celebratory hop behavior.</summary>
    public void SetToneQuiet(BlobatarTone tone)
    {
        _tone = tone;
        _look.Update(0f, AppHost.TryGet<CircadianService>(), tone);
    }

    public void TriggerBounce(double impulse = 0.45) => _physics.Bounce((float)impulse);
    public void TriggerTiltWobble(double degrees = 8.0) => _physics.Wobble((float)degrees);
    public void TriggerBlink(int count = 1) => _physics.Blink(count);
    public void TriggerBoredHop() => _physics.BoredHop();
    public void CancelBoredHop() => _physics.CancelBoredHop();
    public void TriggerStretch() => _physics.Stretch();
    public void TriggerWink(bool leftEye = true) => _physics.Wink(leftEye);
    public void SetHappy(double amount = 1.0, double duration = 1.2) { if (amount > 0) _physics.Happy((float)duration); }

    public void Press() => _physics.Press();
    public void Release() => _physics.Release();
    public void Drop() => _physics.Drop();
    public void CompanionDrop() => _physics.CompanionDrop();
    public void Startle(double stimulusDx) => _physics.Startle((float)stimulusDx);
    public void Proud() => _physics.Proud();
    public void Celebrate() => _physics.Celebrate();
    public void Poke() => _physics.Poke();
    public void SetThinking(bool thinking, double load = 0) { _physics.Thinking = thinking; _physics.Load = (float)load; }
    public void SetDragOver(bool over) => _dragOver = over;
    public bool SuppressNonBoredHops
    {
        get => _physics.SuppressNonBoredHops;
        set => _physics.SuppressNonBoredHops = value;
    }
    public void SetDragMotion(double leanDegrees, double stretch)
    {
        _dragLean = (float)Math.Clamp(leanDegrees, -14, 14);
        _dragStretch = (float)Math.Clamp(stretch, 0, .08);
    }
    public void SetRestLean(double degrees) => _restLean = (float)Math.Clamp(degrees, -8, 8);
    public void SetCompanionFullness(double amount) => _fullness = (float)Math.Clamp(amount, 0, 1);
    public void TriggerCompanionEyeRoll(double degrees = 10, double duration = 1.4)
    {
        _eyeRollDegrees = (float)Math.Clamp(degrees, 0, 18);
        _eyeRollDuration = _eyeRollRemaining = (float)Math.Clamp(duration, .2, 3);
    }
    public void SetCurious(bool curious) => _curious = curious;
    public void SetPaused(bool paused) { _paused = paused; if (paused) _eyeRollRemaining = 0; }
    public void SetNapping(bool napping) { _napping = napping; if (napping) _eyeRollRemaining = 0; }
    public bool CompanionMode { get => _companionMode; set => _companionMode = value; }
    /// <summary>Temporarily prevents a one-eye idle wink during the desktop companion's file animation.</summary>
    public bool SuppressAutonomousWinks { get => _suppressAutonomousWinks; set => _suppressAutonomousWinks = value; }

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
        LookAtCursor(new Point(BlobatarRenderer.FaceX + p.X * 0.45, BlobatarRenderer.BodyCy + p.Y * 0.45), deadzone: 3.5, sensitivity: sensitivity * 0.45);
    }

    /// <summary>Advances the simulation. Returns true while transient motion (or a palette cross-fade) needs frames.</summary>
    public bool Update(double dt, bool motionEnabled = true)
    {
        dt = Math.Clamp(dt, 0.0, 0.25);
        _physics.ReduceMotion = !motionEnabled;

        _look.Update((float)dt, AppHost.TryGet<CircadianService>(), _tone);
        var look = _look.Current;

        var eyeRoll = 0f;
        if (motionEnabled && _companionMode && !_paused && !_napping && _eyeRollRemaining > 0)
        {
            var elapsed = _eyeRollDuration - _eyeRollRemaining;
            var progress = elapsed / _eyeRollDuration;
            eyeRoll = _eyeRollDegrees * MathF.PI / 180f
                * MathF.Sin(elapsed * MathF.Tau * 1.35f) * (1 - Math.Clamp(progress, 0, 1));
            _eyeRollRemaining = MathF.Max(0, _eyeRollRemaining - (float)dt);
        }

        var h = DateTime.Now.TimeOfDay.TotalHours;
        var clockSleepy = !look.Active ? 0f
            : h >= 12 ? BlobMath.Sm(21f, 23.5f, (float)h) : 1f - BlobMath.Sm(4.5f, 6f, (float)h);

        var inp = new BlobatarInput
        {
            Pointer = _pointer, DragOver = _dragOver, Curious = _curious, Paused = _paused, Napping = _napping,
            CompanionMode = _companionMode, SuppressAutonomousWinks = _suppressAutonomousWinks,
            DragLean = _dragLean, DragStretch = _dragStretch, RestLean = _restLean,
            Fullness = _fullness, CompanionEyeRoll = eyeRoll,
            Hover = _hover, ClockSleepy = clockSleepy,
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
    public IBrush Blush = Brushes.Transparent;
    public IBrush ShadowBrush = Brushes.Transparent;
    public IBrush ShadowCoreBrush = Brushes.Transparent;
    public IPen RimPen = null!;
    public IPen GlowPen = null!;
    public IPen OutlinePen = null!;
    public IPen MouthPen = null!;
    public IPen? EyePen;

    private static readonly Color BlushColor = Color.FromRgb(0xFF, 0x8C, 0xAA);

    private static Color A(Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255.0, 0, 255), c.R, c.G, c.B);
    private static byte Mix(byte a, byte b, double t) => (byte)Math.Clamp(a + (b - a) * t, 0, 255);

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
        GlintAmbient = new SolidColorBrush(A(look.Sky, 0.32));

        // Silhouette: a soft dark edge keeps pale bodies readable on a white hub; it fades out at night (the neon rim takes over).
        OutlinePen = new Pen(new SolidColorBrush(A(look.Lo, 0.45 * (1.0 - look.Wn))), 1.1);

        // Night only: a thin neon edge so the dark eyes stay visible on the dark body.
        EyePen = look.Wn > 0.02 ? new Pen(new SolidColorBrush(A(look.Rim, 0.55 * look.Wn)), 0.6) : null;

        // Cheeks: warm pink, slightly quieter on dark glass.
        Blush = new SolidColorBrush(A(BlushColor, BlobMath.Lerp(0.30f, 0.20f, look.Wn)));

        // Smile stroke: eye colour by day, neon rim colour at night.
        var ec = BlobatarRenderer.EyeColor;
        var mouth = Color.FromRgb(Mix(ec.R, look.Rim.R, look.Wn), Mix(ec.G, look.Rim.G, look.Wn), Mix(ec.B, look.Rim.B, look.Wn));
        MouthPen = new Pen(new SolidColorBrush(mouth), 1.2, lineCap: PenLineCap.Round);

        // Pre-cached shadow gradient brushes:
        var shadowColor = look.Wn > 0.1 ? look.Rim : Color.FromRgb(0x18, 0x14, 0x24);
        ShadowBrush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(255, shadowColor.R, shadowColor.G, shadowColor.B), 0.0),
                new GradientStop(Color.FromArgb(0, shadowColor.R, shadowColor.G, shadowColor.B), 1.0),
            },
        };
        ShadowCoreBrush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.0),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0),
            },
        };
    }
}

/// <summary>Hardware-accelerated renderer for the Blobatar mochi. Reads state.Frame / state.Look only.</summary>
public static class BlobatarRenderer
{
    /// <summary>Visual centre of the face and body in viewBox units (the eye pair and the body are both centred here).</summary>
    public const double FaceX = BlobatarLayout.FaceX;
    public const double BodyCy = BlobatarLayout.BodyCy;

    // Plumpness is applied to the body silhouette and to feature POSITIONS only; eyes, glints and cheeks keep their
    // true shapes, so nothing is stretched (the old root matrix skewed eye tilt and squashed every catchlight).
    private const double PlumpX = 1.08, PlumpY = 0.92;

    // Both eyes share one geometry and one baseline (BlobatarLayout levels their rest positions).
    private const double EyeRx = 3.7, EyeRy = 5.9;

    /// <summary>Mochi body, 12 Bézier control points N0 h h N1 h h N2 h h N3 h h (right, bottom, left, top): domed head, soft flat base, widest just below centre, symmetric about <see cref="FaceX"/>.</summary>
    private static readonly System.Numerics.Vector2[] MochiPoints = BlobatarLayout.DefaultBodyPoints;

    private static readonly Point[] BodyRest = ToPoints(MochiPoints);

    private static Point[] ToPoints(System.Numerics.Vector2[] v)
    {
        var p = new Point[v.Length];
        for (var i = 0; i < v.Length; i++) p[i] = new Point(v[i].X, v[i].Y);
        return p;
    }

    internal static System.Numerics.Vector2[] BodyRestPoints => MochiPoints;

    private static Geometry? _bodyGeometry;
    private static Geometry BodyGeometry => _bodyGeometry ??= BuildPath(BodyRest);

    private static Geometry? _eyeGeometry;
    private static Geometry EyeGeometry => _eyeGeometry ??= new EllipseGeometry(new Rect(-EyeRx, -EyeRy, EyeRx * 2, EyeRy * 2));

    private static Geometry? _unitSmileGeometry;
    private static Geometry UnitSmileGeometry => _unitSmileGeometry ??= BuildUnitSmile();
    private static Geometry BuildUnitSmile()
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(-2.6, 0), false);
            c.QuadraticBezierTo(new Point(0, 4.2), new Point(2.6, 0));
            c.EndFigure(false);
        }
        return g;
    }

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

    private static Point Plump(double x, double y) => new(FaceX + (x - FaceX) * PlumpX, BodyCy + (y - BodyCy) * PlumpY);

    /// <summary>Maps a point in control space to the physics viewBox (the inverse of Render's transforms), so the eyes aim exactly at the cursor.</summary>
    internal static Point ToViewBox(Point pointInControl, Rect bounds)
    {
        var scale = Math.Min(bounds.Width, bounds.Height) / 70.0;
        if (scale <= 0.001)
            return new Point(FaceX, BodyCy);

        var x = FaceX + (pointInControl.X - bounds.Center.X) / scale;
        var y = BodyCy + (pointInControl.Y - bounds.Center.Y) / scale;
        return new Point(FaceX + (x - FaceX) / PlumpX, BodyCy + (y - BodyCy) / PlumpY);
    }

    public static void Render(DrawingContext context, Rect bounds, BlobatarState state, float mouthOpen = 0f)
    {
        var size = Math.Min(bounds.Width, bounds.Height);
        var scale = size / 70.0;
        if (scale <= 0.001)
            return;

        // Uniform root: the body's centre lands exactly on the bounds' centre, at any size / DPI.
        var rootMatrix = Matrix.CreateTranslation(-FaceX, -BodyCy) *
                         Matrix.CreateScale(scale, scale) *
                         Matrix.CreateTranslation(bounds.Center.X, bounds.Center.Y);

        // Mochi proportions live here, around the body centre.
        var plump = Matrix.CreateTranslation(-FaceX, -BodyCy) *
                    Matrix.CreateScale(PlumpX, PlumpY) *
                    Matrix.CreateTranslation(FaceX, BodyCy);

        var f = state.Frame;
        var look = state.Look;
        var cache = state.Cache;
        cache.Ensure(look);

        using (context.PushTransform(rootMatrix))
        {
            using (context.PushTransform(plump))
            {
                DrawShadow(context, cache, look, f);

                var body = BuildBody(f);
                using (context.PushTransform(f.Body.ToAvalonia()))
                {
                    // Fill + soft silhouette edge (the rim light below paints over its inner half).
                    context.DrawGeometry(cache.Body, cache.OutlinePen, body);

                    using (context.PushGeometryClip(body))
                    {
                        // Forehead specular, placed toward the key light.
                        var sx = FaceX + look.Light.X * 35.0 * 0.7;
                        var sy = BodyCy + look.Light.Y * 33.0 * 0.7;
                        context.DrawEllipse(cache.Spec, null, new Point(sx, sy), 11, 7);

                        // Cheeks sit under the outer corner of each eye.
                        context.DrawEllipse(cache.Blush, null, new Point(39.9, 54.0), 4.4, 2.5);
                        context.DrawEllipse(cache.Blush, null, new Point(65.2, 54.0), 4.4, 2.5);

                        context.DrawGeometry(null, cache.GlowPen, body);
                        context.DrawGeometry(null, cache.RimPen, body);
                    }

                    if (f.Smile > 0.05f || mouthOpen > 0.015f)
                        DrawMouth(context, cache, f.Smile, mouthOpen);
                }
            }

            // Eyes are drawn outside the body matrix (translation only) so gaze stays exact while the body breathes.
            DrawEye(context, f.Left, 4.8, 4.0, state.LeftEyeTiltDeg, state.LeftForeshortenX, f, state.HappyCurve, look, cache, 0.0);
            DrawEye(context, f.Right, 4.6, 3.8, state.RightEyeTiltDeg, state.RightForeshortenX, f, state.HappyCurve, look, cache, 0.0);
        }
    }

    private static readonly IBrush OpenMouthBrush = new SolidColorBrush(Color.Parse("#392B5A"));
    private static readonly Pen OpenMouthPen = new(new SolidColorBrush(Color.Parse("#6E58A8")), 1.2);

    private static void DrawMouth(DrawingContext context, BlobatarBrushCache cache, double smile, double mouthOpen)
    {
        if (mouthOpen > .015)
        {
            context.DrawEllipse(OpenMouthBrush, OpenMouthPen, new Point(FaceX, 57.0), 3.9, 1.0 + 4.0 * mouthOpen);
            return;
        }
        using (context.PushTransform(Matrix.CreateScale(1.0, smile) * Matrix.CreateTranslation(FaceX, 56.8)))
        using (context.PushOpacity(Math.Min(1.0, smile * 1.5)))
            context.DrawGeometry(null, cache.MouthPen, UnitSmileGeometry);
    }

    private static void DrawShadow(DrawingContext context, BlobatarBrushCache cache, CircadianLookFrame look, BlobatarFrame f)
    {
        var sh = CircadianLook.Shadow(look, f.Sx, f.Sy, f.Hop);
        var at = new Point(FaceX + sh.Dx, 84.5);
        if (sh.Alpha > 0.003 && cache.ShadowBrush is not null)
        {
            using (context.PushOpacity(Math.Clamp(sh.Alpha * 0.8, 0.0, 1.0)))
                context.DrawEllipse(cache.ShadowBrush, null, at, sh.Rx, sh.Ry * 1.3);
        }
        if (sh.CoreAlpha > 0.01 && cache.ShadowCoreBrush is not null)
        {
            using (context.PushOpacity(Math.Clamp(sh.CoreAlpha, 0.0, 1.0)))
                context.DrawEllipse(cache.ShadowCoreBrush, null, at, sh.Rx * 0.55, sh.Ry * 0.8);
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

        return BuildPath(p);
    }

    private static StreamGeometry BuildPath(Point[] p)
    {
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

    private static void DrawEye(DrawingContext context, BlobatarEye e, double maxX, double maxY,
        double tiltDeg, double foreshortenX, BlobatarFrame f, double happy, CircadianLookFrame look,
        BlobatarBrushCache cache, double levelY)
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

        var at = Plump(e.Center.X + nx * maxX, e.Center.Y + ny * maxY - e.Brow * 0.35 + levelY);   // "brow": lift the eye

        var m = Matrix.CreateScale(sx, sy) * Matrix.CreateRotation(rot) * Matrix.CreateTranslation(at.X, at.Y);
        using (context.PushTransform(m))
            context.DrawGeometry(EyeBrush, cache.EyePen, EyeGeometry);

        if (blink <= 0.40)
            return;

        // Primary catchlight tracks the key light (sun by day, moon / neon at night) and slides opposite the gaze.
        // Its height follows the lid so it never hangs outside a half-closed eye.
        double lx = look.Light.X, ly = look.Light.Y;
        var lift = Math.Min(1.0, blink);
        var r1 = (happy > 0.4 ? 1.45 : 1.25) * dil;
        context.DrawEllipse(cache.GlintPrimary, null, new Point(at.X + lx * 1.4 - nx * 0.85, at.Y + ly * 2.5 * lift - ny * 0.55), r1, r1);

        // Secondary: soft ambient glint opposite the key light, rotated 20 degrees.
        const double Rot = 20.0 * Math.PI / 180.0;
        var ax = -(lx * Math.Cos(Rot) - ly * Math.Sin(Rot));
        var ay = -(lx * Math.Sin(Rot) + ly * Math.Cos(Rot));
        var r2 = 0.65 * dil;
        context.DrawEllipse(cache.GlintAmbient, null, new Point(at.X + ax * 1.9 - nx * 0.4, at.Y + ay * 2.6 * lift - ny * 0.3), r2, r2);
    }
}
