using System;
using System.Numerics;

namespace Filee.App.Controls;

public enum BlobatarEvent { Wink, Press, Squish, Jump, Land, Curious, Proud, Celebrate, ThinkTick, Yawn }

/// <summary>
/// Geometry of the mochi in its 100-unit viewBox. The single source of truth: the renderer, the gaze maths and the
/// body contour all read their points and centre from here.
/// </summary>
public sealed class BlobatarLayout
{
    /// <summary>Visual centre of the face and body (the eye pair and the body are both symmetric about FaceX).</summary>
    public const double FaceX = 52.55, BodyCy = 51.18;

    /// <summary>
    /// Mochi body, 12 Bézier control points N0 h h N1 h h N2 h h N3 h h (right, bottom, left, top): a domed head, a soft
    /// flat base and the widest point just below the centre, symmetric about <see cref="FaceX"/>.
    /// </summary>
    public static readonly Vector2[] DefaultBodyPoints = BuildMochiPoints();

    private static Vector2[] BuildMochiPoints()
    {
        const float x0 = (float)FaceX, w = 35f, top = 18.21f, bottom = 84.14f, belly = 56f;
        var lowY = belly + 0.64f * (bottom - belly);
        var upY = belly - 0.56f * (belly - top);
        return
        [
            new(x0 + w, belly), new(x0 + w, lowY), new(x0 + w * 0.70f, bottom), new(x0, bottom),
            new(x0 - w * 0.70f, bottom), new(x0 - w, lowY), new(x0 - w, belly), new(x0 - w, upY),
            new(x0 - w * 0.56f, top), new(x0, top), new(x0 + w * 0.56f, top), new(x0 + w, upY),
        ];
    }

    public Vector2 Anchor = new((float)FaceX, 84.14f); // base contact point: never moves in Y
    public Vector2 EyeRestL = new(41.8f, 44.8f);
    public Vector2 EyeRestR = new(63.3f, 44.8f);       // on the same baseline as the left eye
    public Vector2 EyeMaxL = new(4.8f, 4.0f);          // elliptical gaze limits
    public Vector2 EyeMaxR = new(4.6f, 3.8f);
    public Vector2 NeckPivot = new((float)FaceX, 62f); // head-roll pivot
    public float BodyHeight = 65.93f;                  // 84.14 - 18.21
    public float ProxNear = 40f, ProxFar = 130f;       // cursor proximity range, viewBox units
    public Vector2[]? BodyPoints;                      // rest control points for the contour wobble
}

public struct BlobatarInput
{
    public Vector2? Pointer;     // viewBox units; null = no cursor
    public bool DragOver;
    public bool Curious;
    public bool Paused;
    public bool Napping;
    public bool CompanionMode;
    public bool SuppressAutonomousWinks;
    public float DragLean;     // -14..14 degrees, spring-driven by companion drag velocity
    public float DragStretch;  // 0..0.08, vertical stretch while the companion is moved
    public float RestLean;      // companion-only edge perch; zero for wheel and main-window Blobs
    public float Fullness;      // companion-only transient body fullness, 0..1
    public float CompanionEyeRoll;
    public float Hover;          // 0..1 external proximity boost (donut hover)
    public float ClockSleepy;    // 0..1, only when Circadian is on
    public float Intensity;      // CircadianLookFrame.Intensity
    public float Deadzone, Sensitivity;
}

public struct BlobatarEye
{
    public Vector2 Center;       // body/head motion applied, gaze NOT applied
    public Vector2 Gaze;         // normalised to the eye's elliptical limit (|g| <= 1), saccades included
    public float Scale, Lid, Squint, Roll, PupilRho, Brow;
}

/// <summary>One reused instance; do not keep it across frames.</summary>
public sealed class BlobatarFrame
{
    public Matrix3x2 Body, Head;
    public float Sx, Sy, LeanDeg, Hop, HeadRoll, Smile, TuftL, TuftR, Arousal, Sleepy;
    public Vector2 NeckPivot;
    public BlobatarEye Left, Right;
    public Vector2[]? Contour;   // per-point offsets to ADD to Layout.BodyPoints
}

public static class BlobatarMatrixExtensions
{
    public static Avalonia.Matrix ToAvalonia(this Matrix3x2 m) => new(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);
}

public sealed class BlobatarPhysics
{
    public BlobatarLayout Layout { get; }
    public BlobatarFrame Frame => _frame;
    public event Action<BlobatarEvent, float>? Raised;
    public bool ReduceMotion { get; set; }
    public bool SuppressNonBoredHops { get; set; }
    public bool Thinking { get; set; }
    public float Load { get; set; }

    /// <summary>True while TRANSIENT motion needs frames. Ambient motion (breathing, heartbeat, saccades) is not counted.</summary>
    public bool Animating { get; private set; }

    private const float Kappa = .9f, Gravity = 900f;
    private const float JellyK = 260f, JellyZ = .34f, PressK = 420f, PressZ = .63f;

    private readonly Random _rng = new(7);
    private readonly BlobatarPoseBlender _blend = new();
    private readonly BlobatarFrame _frame = new();
    private readonly MicroSaccade _sacShared, _sacL, _sacR;
    private readonly LidAnim _blink = new(), _winkL = new(), _winkR = new();

    private BlobSpring _q = new(JellyK, JellyZ);          // squash (+) / stretch (-)
    private BlobSpring _prox = new(90f, .8f);
    private BlobSpring _lean = new(200f, .55f);           // degrees
    private BlobSpring _tuftL = new(180f, .26f), _tuftR = new(150f, .28f);
    private BlobSpring _pupil = new(120f, .9f, .57f);
    private BlobSpring _nod = new(110f, .5f);
    private BlobSpring _arousal = new(60f, 1f, .1f);
    private BlobSpring _fullness = new(14f, .9f);
    // Gaze keeps the original constants (k = 340, d ~ 32) so tracking feels exactly as before.
    private BlobSpring _glx = new(340f, .87f), _gly = new(340f, .87f), _grx = new(340f, .87f), _gry = new(340f, .87f);

    private BlobatarPose _pose = BlobatarPose.Idle;
    private Vector2 _cenL, _cenR, _lastPtr;
    private float _idle, _sleepy, _reflex, _biasL, _biasR;
    private float _breathCyc, _hbCyc;
    private double _scanPh; private int _scanK = int.MinValue;
    private float _hop, _hopV, _pendT = -1f, _pendV;
    private float _proudT, _startleT, _happyT, _puffT, _snapT = -1f;
    private float _companionEyeRoll;
    private float _blinkT = 3.6f, _nodT = 10f, _glanceT = 2.8f, _glX, _glY;
    private bool _glActive, _pressed, _prevDrag;
    private int _celebHops, _extraBlinks;
    private float[]? _fi, _ph; private Vector2[]? _contour; private Vector2 _centroid;

    public BlobatarPhysics(BlobatarLayout layout)
    {
        Layout = layout;
        _cenL = layout.EyeRestL; _cenR = layout.EyeRestR;
        _sacShared = new MicroSaccade(_rng); _sacL = new MicroSaccade(_rng); _sacR = new MicroSaccade(_rng);
        if (layout.BodyPoints is { Length: > 0 } pts)
        {
            _contour = new Vector2[pts.Length]; _fi = new float[pts.Length]; _ph = new float[pts.Length];
            for (var i = 0; i < pts.Length; i++)
            {
                _fi[i] = .11f + .08f * (float)_rng.NextDouble();
                _ph[i] = BlobMath.Tau * (float)_rng.NextDouble();
                _centroid += pts[i];
            }
            _centroid /= pts.Length;
        }
    }

    // ------------------------------------------------ events from outside

    public void Press()
    {
        _pressed = true; _idle = 0f;
        _q.Tune(PressK, PressZ);
        _reflex = .15f;
        Raise(BlobatarEvent.Press, 0f);
    }

    public void Release()
    {
        if (!_pressed) return;
        var intensity = Math.Clamp(_q.X / .14f, 0f, 1.2f);
        _pressed = false; _idle = 0f;
        _q.Tune(JellyK, JellyZ);
        _q.Kick(-3.2f);
        Raise(BlobatarEvent.Squish, intensity);
    }

    /// <summary>Silent jelly kick (legacy TriggerBounce): positive impulse = stretch first.</summary>
    public void Bounce(float impulse)
    {
        if (!_pressed) _q.Tune(JellyK, JellyZ);
        _q.Kick(-impulse * 4f);
    }

    public void Wobble(float degrees) => _lean.Kick(degrees * 16f);

    public void Blink(int count = 1)
    {
        _blink.Fire(.05f, 0f, .07f);
        _extraBlinks = Math.Max(0, count - 1);
    }

    public void Wink(bool left, bool raise = true)
    {
        _idle = 0f;
        (left ? _winkL : _winkR).Fire(.06f, .14f, .12f);
        if (raise) Raise(BlobatarEvent.Wink, left ? -1f : 1f);
    }

    public void Happy(float seconds) { _happyT = MathF.Max(_happyT, seconds); }

    public void Drop()
    {
        _idle = 0f;
        _q.Tune(JellyK, JellyZ);
        _q.Kick(4f);                      // the gulp
        _pendT = .10f; _pendV = 95f;
    }

    /// <summary>A companion-only swallow squash; the wheel keeps its established gulp-and-hop Drop behavior.</summary>
    public void CompanionDrop()
    {
        _idle = 0f;
        _q.Tune(JellyK, JellyZ);
        _q.Kick(2.2f);
        _pendT = -1f; _pendV = 0f;
    }

    /// <summary>stimulusDx: + = the stimulus came from the right.</summary>
    public void Startle(float stimulusDx)
    {
        _idle = 0f;
        var s = stimulusDx >= 0f ? 1f : -1f;
        if (s > 0f) { _biasR = -.5f; _biasL = -4.5f; } else { _biasL = -.5f; _biasR = -4.5f; }
        _lean.Kick(-s * 40f);
        _startleT = .9f; _reflex = .1f;
        if (!SuppressNonBoredHops) Launch(60f);
    }

    public void Celebrate()
    {
        _idle = 0f; _happyT = 2.2f; _celebHops = SuppressNonBoredHops ? 0 : 2;
        Raise(BlobatarEvent.Celebrate, 0f);
        if (!SuppressNonBoredHops) Launch(95f);
    }

    public void Proud()
    {
        _idle = 0f; _proudT = 1.4f; _puffT = .25f;
        if (!SuppressNonBoredHops) Launch(55f);
        Raise(BlobatarEvent.Proud, 0f);
    }

    public void Poke() => _idle = 0f;

    /// <summary>A small autonomous hop used only by the desktop companion's boredom behavior.</summary>
    public void BoredHop()
    {
        _idle = 0f;
        _q.Tune(JellyK, JellyZ);
        Launch(48f);
    }

    public void CancelBoredHop()
    {
        _pendT = -1f;
        _pendV = 0f;
        _hop = 0f;
        _hopV = 0f;
    }

    /// <summary>A quiet stretch without the celebratory hop or sound cues.</summary>
    public void Stretch()
    {
        _idle = 0f;
        _q.Tune(JellyK, JellyZ);
        _q.Kick(-2.4f);
    }

    // ------------------------------------------------ fixed-step simulation

    public void Step(float dt, in BlobatarInput inp)
    {
        var lay = Layout;
        _idle += dt;

        var ptr = inp.Pointer;
        if (ptr is Vector2 pp && Vector2.DistanceSquared(pp, _lastPtr) > 4f) { _lastPtr = pp; _idle = 0f; }
        var drag = inp.DragOver;
        if (drag && !_prevDrag) { Raise(BlobatarEvent.Curious, 0f); _idle = 0f; }
        _prevDrag = drag;
        _companionEyeRoll = inp.CompanionMode && !inp.Paused && !inp.Napping ? inp.CompanionEyeRoll : 0f;

        var idleSleepy = inp.CompanionMode ? BlobMath.Sm(180f, 240f, _idle) : BlobMath.Sm(30f, 90f, _idle);
        _sleepy = inp.Paused || inp.Napping ? 1f : MathF.Max(Math.Clamp(inp.ClockSleepy, 0f, 1f), idleSleepy);

        // proximity + lean
        var center = new Vector2(lay.Anchor.X, lay.Anchor.Y - lay.BodyHeight * .5f);
        float p = 0f, dx = 0f;
        if (ptr is Vector2 pt)
        {
            p = BlobMath.Sm(lay.ProxFar, lay.ProxNear, Vector2.Distance(pt, center));
            dx = pt.X - lay.Anchor.X;
        }
        p = MathF.Max(p, Math.Clamp(inp.Hover, 0f, 1f));
        if (drag) p = 1f;
        _prox.Target = 0f; // Keep natural body aspect ratio; do not stretch vertically on proximity/drag
        _lean.Target = Math.Clamp(Math.Clamp(dx / lay.ProxFar, -1f, 1f) * 4f * p + inp.DragLean
            + (inp.CompanionMode ? inp.RestLean : 0f), -15f, 15f);
        _fullness.Target = inp.CompanionMode ? Math.Clamp(inp.Fullness, 0f, 1f) : 0f;

        // expressions
        _blend.Set(BlobatarExpression.Curious, drag || inp.Curious ? 1f : 0f);
        _blend.Set(BlobatarExpression.Sleepy, _sleepy);
        _blend.Set(BlobatarExpression.Thinking, Thinking ? 1f : 0f);
        _blend.Set(BlobatarExpression.Proud, Active(ref _proudT, dt));
        _blend.Set(BlobatarExpression.Startled, Active(ref _startleT, dt));
        _blend.Set(BlobatarExpression.Happy, Active(ref _happyT, dt));
        var blendMoving = _blend.Step(dt);
        _pose = _blend.Current;

        if (_puffT > 0f) _puffT -= dt;
        _q.Target = _pressed ? .14f : Math.Clamp((_puffT > 0f ? -.05f : 0f) - inp.DragStretch, -.12f, 0f);

        // arousal -> breathing / heartbeat rate
        _arousal.Target = Math.Clamp(_pose.Arousal + .3f * p, 0f, 1f);
        _arousal.Step(dt);
        var A = Math.Clamp(_arousal.X, 0f, 1f);
        _breathCyc = (_breathCyc + BlobMath.Lerp(.24f + .5f * A, .15f, _sleepy) * dt) % 1f;
        _hbCyc = (_hbCyc + (1.1f + 1.4f * A) * dt) % 1f;

        // tufts lag against body motion, then relax to the mood target
        _tuftL.V += -_q.V * 60f * dt; _tuftR.V += -_q.V * 60f * dt;
        _biasL *= MathF.Exp(-dt / .5f); _biasR *= MathF.Exp(-dt / .5f);
        if (MathF.Abs(_biasL) < .02f) _biasL = 0f;
        if (MathF.Abs(_biasR) < .02f) _biasR = 0f;
        _tuftL.Target = _pose.TuftL + _biasL;
        _tuftR.Target = _pose.TuftR + _biasR;

        _reflex *= MathF.Exp(-dt / .6f);
        if (_reflex < .002f) _reflex = 0f;

        // hop (ballistic)
        if (_pendT > 0f) { _pendT -= dt; if (_pendT <= 0f && _hop <= 0f) Launch(_pendV); }
        if (_hop > 0f || _hopV > 0f)
        {
            _hopV -= Gravity * dt; _hop += _hopV * dt;
            if (_hop <= 0f && _hopV < 0f)
            {
                var imp = -_hopV; _hop = 0f; _hopV = 0f;
                _q.Tune(JellyK, JellyZ); _q.Kick(.04f * imp);
                _tuftL.Kick(-10f); _tuftR.Kick(-10f);
                Raise(BlobatarEvent.Land, imp);
                if (_celebHops > 0) { _celebHops--; _pendT = .08f; _pendV = 70f; }
            }
        }

        // blinking
        _blinkT -= dt;
        if (_blinkT <= 0f)
        {
            if (_rng.NextDouble() < .12 && !inp.SuppressAutonomousWinks) Wink(_rng.NextDouble() < .5, raise: false); // silent autonomous wink
            else _blink.Fire(BlobMath.Lerp(.09f, .18f, _sleepy), BlobMath.Lerp(.04f, .12f, _sleepy), BlobMath.Lerp(.14f, .34f, _sleepy));
            _blinkT = 3.2f + (float)_rng.NextDouble() * 2.4f;
        }
        if (_extraBlinks > 0 && !_blink.Running) { _blink.Fire(.05f, 0f, .07f); _extraBlinks--; }
        _blink.Step(dt); _winkL.Step(dt); _winkR.Step(dt);

        // sleepy nod / yawn / snap awake
        if (!inp.Paused && !inp.Napping && _sleepy > .5f)
        {
            _nodT -= dt;
            if (_nodT <= 0f)
            {
                _nodT = 8f + (float)_rng.NextDouble() * 6f;
                _nod.Kick(-.9f); _q.Kick(1.2f);
                if (_sleepy > .7f && _rng.NextDouble() < .3) Raise(BlobatarEvent.Yawn, 0f);
                if (_rng.NextDouble() < .2) _snapT = .6f;
            }
        }
        if (_snapT > 0f) { _snapT -= dt; if (_snapT <= 0f) Startle(0f); }

        // thinking scan
        if (Thinking)
        {
            var f = .7f * (1f + .8f * Math.Clamp(Load, 0f, 1f));
            _scanPh += BlobMath.Tau * f * dt;
            var k = (int)Math.Floor((_scanPh + Math.PI / 2.0) / Math.PI);
            if (_scanK == int.MinValue) _scanK = k;
            if (k != _scanK) { _scanK = k; Raise(BlobatarEvent.ThinkTick, 0f); }
        }
        else _scanK = int.MinValue;

        // idle glances (only without a cursor)
        if (!inp.Paused && !inp.Napping && ptr is null && !Thinking && !ReduceMotion)
        {
            _glanceT -= dt;
            if (_glanceT <= 0f)
            {
                if (!_glActive)
                {
                    _glX = ((float)_rng.NextDouble() - .5f) * .34f; _glY = ((float)_rng.NextDouble() - .5f) * .28f;
                    _glActive = true; _glanceT = .6f + (float)_rng.NextDouble() * .4f;
                }
                else { _glX = _glY = 0f; _glActive = false; _glanceT = 2.6f + (float)_rng.NextDouble() * 3f; }
            }
        }
        else if (ptr is not null) { _glX = _glY = 0f; _glActive = false; _glanceT = 2.8f; }

        // gaze targets: aimed from the eyes' moved centres (breathing / squash / lean included)
        Vector2 gl, gr;
        if (Thinking)
            gl = gr = new Vector2(.6f * (float)Math.Sin(_scanPh), .15f * (float)Math.Sin(2.0 * _scanPh + .6));
        else
        {
            var g = new Vector2(_glX, _glY);
            gl = Aim(ptr, _cenL, lay.EyeMaxL, inp.Deadzone, inp.Sensitivity) + g;
            gr = Aim(ptr, _cenR, lay.EyeMaxR, inp.Deadzone, inp.Sensitivity) + g;
        }
        _glx.Target = gl.X; _gly.Target = gl.Y; _grx.Target = gr.X; _gry.Target = gr.Y;
        var mv = false;
        mv |= Move(ref _glx, dt, .002f, .01f); mv |= Move(ref _gly, dt, .002f, .01f);
        mv |= Move(ref _grx, dt, .002f, .01f); mv |= Move(ref _gry, dt, .002f, .01f);

        // micro-saccades: fixation only (no cursor), never when reduced motion
        var sac = !ReduceMotion && ptr is null;
        var speed = MathF.Sqrt(_glx.V * _glx.V + _gly.V * _gly.V);
        _sacShared.Update(dt, speed, sac); _sacL.Update(dt, speed, sac); _sacR.Update(dt, speed, sac);

        // pupils (eye size): rho = .55 + .25(1-I) + .20A - .08S - reflex + .4 startle - .05 thinking
        var I = Math.Clamp(inp.Intensity <= 0f ? 1f : inp.Intensity, 0f, 1f);
        var rho = .55f + .25f * (1f - I) + .20f * A - .08f * _sleepy - _reflex
                  + .40f * _blend.Weight(BlobatarExpression.Startled)
                  - .05f * _blend.Weight(BlobatarExpression.Thinking);
        _pupil.Target = Math.Clamp(rho, .35f, .95f);

        if (_ph is not null && !ReduceMotion)
            for (var i = 0; i < _ph.Length; i++) _ph[i] = (_ph[i] + BlobMath.Tau * _fi![i] * dt) % BlobMath.Tau;

        // remaining springs
        mv |= Move(ref _q, dt, .002f, .01f); mv |= Move(ref _prox, dt, .002f, .01f);
        mv |= Move(ref _fullness, dt, .002f, .01f);
        mv |= Move(ref _lean, dt, .03f, .5f);
        mv |= Move(ref _tuftL, dt, .02f, .2f); mv |= Move(ref _tuftR, dt, .02f, .2f);
        mv |= Move(ref _nod, dt, .002f, .02f); mv |= Move(ref _pupil, dt, .002f, .02f);
        mv |= Move(ref _arousal, dt, .002f, .02f);

        mv |= blendMoving || _hop > 0f || _hopV > 0f || _pendT > 0f || _celebHops > 0
              || _blink.Running || _winkL.Running || _winkR.Running || _extraBlinks > 0
              || _happyT > 0f || _proudT > 0f || _startleT > 0f || _puffT > 0f || _snapT > 0f
              || _reflex > 0f || _biasL != 0f || _biasR != 0f || Thinking;
        Animating = mv;
    }

    // ------------------------------------------------ per-frame output

    public BlobatarFrame Compose()
    {
        var lay = Layout; var f = _frame;
        var A = Math.Clamp(_arousal.X, 0f, 1f);
        var S = _sleepy;

        var qT = Math.Clamp(_q.X + _prox.X, -.35f, .35f);
        var ab = ReduceMotion ? 0f : .012f * BlobMath.Lerp(1f, 1.3f, S);
        var ah = ReduceMotion ? 0f : .006f * A;
        var phase = BlobMath.Tau * _breathCyc;
        var baseSy = MathF.Exp(-qT) * (1f + .018f * Math.Clamp(_fullness.X, 0f, 1f)) * (1f + ah * Pulse(_hbCyc));
        var sy = baseSy * (1f + ab * MathF.Sin(phase));
        var syH = baseSy * (1f + ab * MathF.Sin(phase - .35f));        // head lags 0.35 rad
        var leanDeg = Math.Clamp(_lean.X, -15f, 15f);
        var lean = leanDeg * MathF.PI / 180f;

        Matrix3x2 Build(float s)
        {
            var sx = MathF.Pow(s, -Kappa);
            return Matrix3x2.CreateTranslation(-lay.Anchor)
                 * Matrix3x2.CreateScale(sx, s)
                 * new Matrix3x2(1f, 0f, -MathF.Tan(lean), 1f, 0f, 0f)    // shear about the base
                 * Matrix3x2.CreateTranslation(lay.Anchor)
                 * Matrix3x2.CreateTranslation(0f, -_hop);
        }

        var mB = Build(sy); var mH = Build(syH);
        f.Body = mB; f.Head = mH; f.Sy = sy; f.Sx = MathF.Pow(sy, -Kappa); f.Hop = _hop; f.LeanDeg = leanDeg;

        var roll = _pose.HeadRoll + _nod.X;
        var neck = Vector2.Transform(lay.NeckPivot, mH);
        var rot = Matrix3x2.CreateRotation(roll, neck);
        f.NeckPivot = neck; f.HeadRoll = roll;

        var thinkSq = _blend.Weight(BlobatarExpression.Thinking) * .18f * MathF.Pow(MathF.Abs(MathF.Sin((float)_scanPh)), 3f);

        _cenL = Vector2.Transform(Vector2.Transform(lay.EyeRestL, mH), rot);
        _cenR = Vector2.Transform(Vector2.Transform(lay.EyeRestR, mH), rot);

        f.Left = new BlobatarEye
        {
            Center = _cenL, Scale = _pose.ScaleL,
            Gaze = ClampUnit(new Vector2(_glx.X, _gly.X) + _sacShared.Offset * .7f + _sacL.Offset * .3f),
            Lid = MathF.Max(_pose.LidL, MathF.Max(_blink.Value, _winkL.Value)),
            Squint = _pose.SquintL + thinkSq, Roll = _pose.RollL + _companionEyeRoll, PupilRho = _pupil.X * 1.01f, Brow = _pose.BrowL,
        };
        f.Right = new BlobatarEye
        {
            Center = _cenR, Scale = _pose.ScaleR,
            Gaze = ClampUnit(new Vector2(_grx.X, _gry.X) + _sacShared.Offset * .7f + _sacR.Offset * .3f),
            Lid = MathF.Max(_pose.LidR, MathF.Max(_blink.Value, _winkR.Value)),
            Squint = _pose.SquintR + thinkSq, Roll = _pose.RollR + _companionEyeRoll, PupilRho = _pupil.X * .99f, Brow = _pose.BrowR,
        };

        f.Smile = Math.Clamp(MathF.Max(_pose.Smile, .12f * Math.Clamp(_fullness.X, 0f, 1f)), 0f, 1f);
        f.TuftL = _tuftL.X; f.TuftR = _tuftR.X;
        f.Arousal = A; f.Sleepy = S;
        f.Contour = ComputeContour(A);
        return f;
    }

    // ------------------------------------------------ helpers

    private static bool Move(ref BlobSpring s, float dt, float tol, float vtol)
    {
        s.Step(dt);
        if (s.Settled(tol, vtol)) { s.X = s.Target; s.V = 0f; return false; }
        return true;
    }

    /// <summary>
    /// Same maths as the original ComputeEyeGaze (elliptical limit, tanh response, near-field deadzone), but returns
    /// the gaze normalised to the eye's ellipse.
    /// </summary>
    private static Vector2 Aim(Vector2? ptr, Vector2 eye, Vector2 max, float dz, float sens)
    {
        if (ptr is not Vector2 p) return Vector2.Zero;
        float dx = p.X - eye.X, dy = p.Y - eye.Y;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < 1e-4f) return Vector2.Zero;
        float ux = dx / dist, uy = dy / dist;
        var inv = ux * ux / (max.X * max.X) + uy * uy / (max.Y * max.Y);
        var bound = inv > 1e-4f ? 1f / MathF.Sqrt(inv) : max.X;
        var t = dist < dz ? dist / dz : 1f;
        var near = dist < dz ? t * t * (3f - 2f * t) : 1f;
        var gain = MathF.Tanh(dist / sens) * near;
        return new Vector2(ux * bound * gain / max.X, uy * bound * gain / max.Y);
    }

    private static Vector2 ClampUnit(Vector2 v) { var l = v.Length(); return l > 1f ? v / l : v; }

    private static float Pulse(float phi) =>
        MathF.Exp(-MathF.Pow(phi / .045f, 2f)) + .55f * MathF.Exp(-MathF.Pow((phi - .16f) / .055f, 2f));

    private static float Active(ref float t, float dt)
    {
        if (t > 0f) { t -= dt; return 1f; }
        return 0f;
    }

    private void Launch(float v0)
    {
        if (_hop > 0f) return;
        _hopV = v0; _hop = .0001f;
        _tuftL.Kick(22f); _tuftR.Kick(22f);
        Raise(BlobatarEvent.Jump, v0);
    }

    private void Raise(BlobatarEvent e, float x) => Raised?.Invoke(e, x);

    private Vector2[]? ComputeContour(float A)
    {
        var pts = Layout.BodyPoints;
        if (pts is null || _contour is null || ReduceMotion) return null;
        var aw = .6f + 1.0f * A;
        var breathWave = MathF.Sin(BlobMath.Tau * _breathCyc);
        for (var i = 0; i < pts.Length; i++)
        {
            var w = Math.Clamp((Layout.Anchor.Y - pts[i].Y) / Layout.BodyHeight, 0f, 1f);   // the base stays pinned
            var dir = pts[i] - _centroid; var l = dir.Length();
            dir = l > 1e-3f ? dir / l : Vector2.Zero;

            // Multi-harmonic fluid jelly physics:
            var h1 = MathF.Sin(_ph![i]);
            var h2 = breathWave * 0.45f;
            var angle = MathF.Atan2(dir.Y, dir.X);
            var h3 = MathF.Sin(angle * 2.0f + BlobMath.Tau * _breathCyc * 1.5f) * 0.25f;

            _contour[i] = dir * (aw * (h1 + h2 + h3) * w);
        }
        return _contour;
    }

    // ------------------------------------------------ nested

    private sealed class LidAnim
    {
        private float _t = -1f, _c, _h, _o;
        public float Value;
        public bool Running => _t >= 0f;

        public void Fire(float close, float hold, float open) { _t = 0f; _c = close; _h = hold; _o = open; }

        public void Step(float dt)
        {
            if (_t < 0f) { Value = 0f; return; }
            _t += dt;
            if (_t < _c) { var x = _t / _c; Value = x * x * (3f - 2f * x); }
            else if (_t < _c + _h) Value = 1f;
            else if (_t < _c + _h + _o) { var x = (_t - _c - _h) / _o; Value = 1f - x * x * (3f - 2f * x); }
            else { Value = 0f; _t = -1f; }
        }
    }

    private sealed class MicroSaccade
    {
        private BlobSpring _x = new(2500f, .95f), _y = new(2500f, .95f);
        private readonly Random _r;
        private float _timer, _ret = -1f, _p1, _p2, _p3, _p4;
        public Vector2 Offset;

        public MicroSaccade(Random r)
        {
            _r = r; _timer = Next();
            _p1 = Rand() * BlobMath.Tau; _p2 = Rand() * BlobMath.Tau; _p3 = Rand() * BlobMath.Tau; _p4 = Rand() * BlobMath.Tau;
        }

        private float Rand() => (float)_r.NextDouble();
        private float Next() => Math.Clamp(-MathF.Log(1f - Rand()) / 1.8f, .15f, 1.2f);    // Poisson, lambda 1.8 / s

        public void Update(float dt, float gazeSpeed, bool enabled)
        {
            if (!enabled) { _x.Snap(0f); _y.Snap(0f); Offset = Vector2.Zero; _ret = -1f; return; }
            _timer -= dt;
            if (_ret > 0f) { _ret -= dt; if (_ret <= 0f) { _x.Target *= .35f; _y.Target *= .35f; } }   // corrective
            if (_timer <= 0f)
            {
                _timer = Next();
                if (gazeSpeed < .15f)
                {
                    var a = BlobMath.Lerp(.02f, .06f, Rand()); var th = BlobMath.Tau * Rand();
                    _x.Target = a * MathF.Cos(th); _y.Target = a * MathF.Sin(th); _ret = .12f;
                }
            }
            _x.Step(dt); _y.Step(dt);
            _p1 = (_p1 + BlobMath.Tau * .31f * dt) % BlobMath.Tau; _p2 = (_p2 + BlobMath.Tau * .83f * dt) % BlobMath.Tau;
            _p3 = (_p3 + BlobMath.Tau * .27f * dt) % BlobMath.Tau; _p4 = (_p4 + BlobMath.Tau * .71f * dt) % BlobMath.Tau;
            Offset = new Vector2(_x.X + .004f * (MathF.Sin(_p1) + .6f * MathF.Sin(_p2)),
                                 _y.X + .004f * (MathF.Sin(_p3) + .6f * MathF.Sin(_p4)));
        }
    }
}
