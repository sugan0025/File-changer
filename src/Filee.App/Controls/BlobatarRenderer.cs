using System;
using Avalonia;
using Avalonia.Media;
using Filee.App.Services;

namespace Filee.App.Controls;

/// <summary>
/// The 5 trait tones defined for Filee mascot states:
/// Idle (0.71, Royal Amethyst), Alert (0.49, Electric Lilac),
/// Anticipation (0.10, Ethereal Frost), Busy (0.28, Muted Slate),
/// Celebration (0.865, Radiant Celestial).
/// </summary>
public enum BlobatarTone
{
    Idle,          // 0.71  -> #8471E2
    Alert,         // 0.49  -> #A499F7
    Anticipation,  // 0.10  -> #CECAFF
    Busy,          // 0.28  -> #D6D5E9
    Celebration    // 0.865 -> #D1CEFF
}

/// <summary>
/// State machine for a Blobatar character instance.
/// The body blob stays completely still and anchored around all sides while tracking the mouse,
/// with only the eyes gliding smoothly to follow the cursor across the window.
/// Features autonomous blinking, interactive click squish bounce, winking, and smooth tone transitions.
/// </summary>
public sealed class BlobatarState
{
    // Physics & Gaze (Independent line of sight for each eye with natural optical convergence)
    public double LeftGazeX { get; private set; }
    public double LeftGazeY { get; private set; }
    public double RightGazeX { get; private set; }
    public double RightGazeY { get; private set; }

    // Combined gaze properties for backwards compatibility
    public double GazeX => (LeftGazeX + RightGazeX) / 2.0;
    public double GazeY => (LeftGazeY + RightGazeY) / 2.0;

    // Expressive Directional Gaze Attitudes & 3D Spherical Head Perspective Foreshortening
    public double LeftEyeTiltDeg => Math.Clamp(LeftGazeX / 4.8, -1.0, 1.0) * 8.0 - Math.Clamp(LeftGazeY / 4.0, -1.0, 1.0) * 3.5 * Math.Sign(LeftGazeX);
    public double RightEyeTiltDeg => Math.Clamp(RightGazeX / 4.6, -1.0, 1.0) * 8.0 - Math.Clamp(RightGazeY / 3.8, -1.0, 1.0) * 3.5 * Math.Sign(RightGazeX);
    public double LeftForeshortenX => LeftGazeX > 0 ? (1.0 - Math.Clamp(LeftGazeX / 4.8, 0.0, 1.0) * 0.18) : (1.0 + Math.Clamp(-LeftGazeX / 4.8, 0.0, 1.0) * 0.04);
    public double RightForeshortenX => RightGazeX < 0 ? (1.0 - Math.Clamp(-RightGazeX / 4.6, 0.0, 1.0) * 0.18) : (1.0 + Math.Clamp(RightGazeX / 4.6, 0.0, 1.0) * 0.04);

    private double _leftGazeVx;
    private double _leftGazeVy;
    private double _rightGazeVx;
    private double _rightGazeVy;

    private double _targetLeftGazeX;
    private double _targetLeftGazeY;
    private double _targetRightGazeX;
    private double _targetRightGazeY;

    private double _idleGazeTimer = 2.8;
    private bool _hasExternalGaze;

    // Head / Body Tilt (Kept 0 during gaze tracking so blob stays completely still)
    public double TiltAngle { get; private set; }
    private double _tiltVelocity;

    // Blinking & Winking (per-eye support with backward-compatible BlinkScaleY)
    public double BlinkScaleY => Math.Min(LeftBlinkScaleY, RightBlinkScaleY);
    public double LeftBlinkScaleY { get; private set; } = 1.0;
    public double RightBlinkScaleY { get; private set; } = 1.0;
    private double _blinkTimer = 3.6;
    private double _blinkElapsed;
    private bool _isBlinking;
    private int _consecutiveBlinks;
    private bool _isWinking;
    private bool _winkLeftEye;

    // Joyful celebration / tap expression (smiling eyes & glints)
    public double HappyCurve { get; private set; }
    private double _targetHappy;
    private double _happyTimer;

    // Squish spring (Only on direct click/tap or hover, NEVER on mouse tracking)
    public double ScaleX { get; private set; } = 1.0;
    public double ScaleY { get; private set; } = 1.0;
    public double FloatY { get; private set; }
    private double _squash = 1.0;
    private double _squashVelocity;

    // Color interpolation
    public Color CurrentColor { get; private set; } = BlobatarRenderer.ColorForTone(BlobatarTone.Idle);
    private Color _targetColor = BlobatarRenderer.ColorForTone(BlobatarTone.Idle);

    public void SetTone(BlobatarTone tone)
    {
        _targetColor = BlobatarRenderer.ColorForTone(tone);
        if (tone == BlobatarTone.Celebration)
        {
            SetHappy(1.0, duration: 2.2);
            TriggerBounce(0.70);
            TriggerBlink(3);
        }
    }

    public void TriggerBounce(double impulse = 0.45)
    {
        _squashVelocity += impulse;
    }

    public void TriggerTiltWobble(double degrees = 8.0)
    {
        _tiltVelocity += degrees * 16.0;
    }

    public void TriggerBlink(int count = 1)
    {
        _isBlinking = true;
        _isWinking = false;
        _blinkElapsed = 0;
        _consecutiveBlinks = Math.Max(0, count - 1);
    }

    public void TriggerWink(bool leftEye = true)
    {
        _isBlinking = true;
        _isWinking = true;
        _winkLeftEye = leftEye;
        _blinkElapsed = 0;
        _consecutiveBlinks = 0;
    }

    public void SetHappy(double amount = 1.0, double duration = 1.2)
    {
        _targetHappy = Math.Clamp(amount, 0.0, 1.0);
        _happyTimer = duration;
    }

    /// <summary>
    /// Accurately directs both eyes toward a cursor coordinate in the mascot's 100x100 viewBox space.
    /// Each eye calculates its line of sight independently from its own socket center, giving natural optical
    /// convergence, smooth near-field easing, and strictly enforced elliptical boundaries so pupils never clip.
    /// </summary>
    public void LookAtCursor(Point? cursorInViewBox, double deadzone = 4.5, double sensitivity = 50.0)
    {
        if (cursorInViewBox is not { } c)
        {
            _hasExternalGaze = false;
            _targetLeftGazeX = 0;
            _targetLeftGazeY = 0;
            _targetRightGazeX = 0;
            _targetRightGazeY = 0;
            return;
        }

        _hasExternalGaze = true;

        // Left Eye center is at (41.8, 44.8)
        ComputeEyeGaze(c.X - 41.8, c.Y - 44.8, maxX: 4.8, maxY: 4.0, deadzone, sensitivity,
            out _targetLeftGazeX, out _targetLeftGazeY);

        // Right Eye center is at (63.3, 45.7)
        ComputeEyeGaze(c.X - 63.3, c.Y - 45.7, maxX: 4.6, maxY: 3.8, deadzone, sensitivity,
            out _targetRightGazeX, out _targetRightGazeY);
    }

    /// <summary>
    /// Helper to compute gaze vector with elliptical boundary confinement and smooth near-field easing.
    /// </summary>
    private static void ComputeEyeGaze(double dx, double dy, double maxX, double maxY,
        double deadzone, double sensitivity, out double targetX, out double targetY)
    {
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 0.0001)
        {
            targetX = 0;
            targetY = 0;
            return;
        }

        var ux = dx / dist;
        var uy = dy / dist;

        // Elliptical boundary limit in direction (ux, uy)
        var invBoundSq = (ux * ux) / (maxX * maxX) + (uy * uy) / (maxY * maxY);
        var boundRadius = invBoundSq > 0.0001 ? 1.0 / Math.Sqrt(invBoundSq) : maxX;

        // Near-field deadzone easing (kills singularity at eye center)
        var near = dist < deadzone ? (dist / deadzone) * (dist / deadzone) * (3.0 - 2.0 * (dist / deadzone)) : 1.0;

        // Smooth distance response curve
        var gain = Math.Tanh(dist / sensitivity) * near;

        targetX = ux * boundRadius * gain;
        targetY = uy * boundRadius * gain;
    }

    /// <summary>
    /// Look at a target relative to the mascot's visual center (e.g. for radial slices or directional offsets).
    /// </summary>
    public void LookAt(Point? cursorRelativeToCenter, double sensitivity = 130.0)
    {
        if (cursorRelativeToCenter is not { } p)
        {
            LookAtCursor(null);
            return;
        }

        if (Math.Abs(p.X) < 0.001 && Math.Abs(p.Y) < 0.001)
        {
            LookAtCursor(null);
            return;
        }

        // Map offset relative to visual center (50.0, 52.5) into viewBox coordinates
        var cursorInViewBox = new Point(50.0 + p.X * 0.45, 52.5 + p.Y * 0.45);
        LookAtCursor(cursorInViewBox, deadzone: 3.5, sensitivity: sensitivity * 0.45);
    }

    /// <summary>
    /// Steps eye gaze pursuit, blinking, and active click squish recovery.
    /// The body blob stays completely still during gaze tracking.
    /// </summary>
    public bool Update(double dt, bool motionEnabled = true)
    {
        var active = false;

        // 1. Color interpolation
        if (CurrentColor != _targetColor)
        {
            var blend = Math.Min(1.0, 1.0 - Math.Exp(-12.0 * dt));
            CurrentColor = Color.FromArgb(
                (byte)(CurrentColor.A + (_targetColor.A - CurrentColor.A) * blend),
                (byte)(CurrentColor.R + (_targetColor.R - CurrentColor.R) * blend),
                (byte)(CurrentColor.G + (_targetColor.G - CurrentColor.G) * blend),
                (byte)(CurrentColor.B + (_targetColor.B - CurrentColor.B) * blend));
            active = true;
        }

        // 2. Idle Autonomous Eye Micro-Glances (Saccades)
        if (!_hasExternalGaze && motionEnabled)
        {
            _idleGazeTimer -= dt;
            if (_idleGazeTimer <= 0)
            {
                if (Math.Abs(_targetLeftGazeX) < 0.05 && Math.Abs(_targetLeftGazeY) < 0.05)
                {
                    // Eyes glance slightly to an angle
                    var gx = (Random.Shared.NextDouble() - 0.5) * 1.6;
                    var gy = (Random.Shared.NextDouble() - 0.5) * 1.1;
                    _targetLeftGazeX = gx;
                    _targetRightGazeX = gx;
                    _targetLeftGazeY = gy;
                    _targetRightGazeY = gy;
                    _idleGazeTimer = 0.6 + Random.Shared.NextDouble() * 0.4;
                }
                else
                {
                    // Eyes return to center
                    _targetLeftGazeX = 0;
                    _targetRightGazeX = 0;
                    _targetLeftGazeY = 0;
                    _targetRightGazeY = 0;
                    _idleGazeTimer = 2.6 + Random.Shared.NextDouble() * 3.0;
                }
            }
        }

        // 3. Independent Eye Gaze pursuit springs (k=340, d=32) - ONLY moves the eyes!
        const double kGaze = 340.0, dGaze = 32.0;

        var leftDist = Math.Abs(_targetLeftGazeX - LeftGazeX) + Math.Abs(_targetLeftGazeY - LeftGazeY);
        if (leftDist > 0.006 || Math.Abs(_leftGazeVx) > 0.02 || Math.Abs(_leftGazeVy) > 0.02)
        {
            var fxL = kGaze * (_targetLeftGazeX - LeftGazeX) - dGaze * _leftGazeVx;
            var fyL = kGaze * (_targetLeftGazeY - LeftGazeY) - dGaze * _leftGazeVy;
            _leftGazeVx += fxL * dt;
            _leftGazeVy += fyL * dt;
            LeftGazeX += _leftGazeVx * dt;
            LeftGazeY += _leftGazeVy * dt;
            active = true;
        }
        else
        {
            LeftGazeX = _targetLeftGazeX;
            LeftGazeY = _targetLeftGazeY;
            _leftGazeVx = 0;
            _leftGazeVy = 0;
        }

        var rightDist = Math.Abs(_targetRightGazeX - RightGazeX) + Math.Abs(_targetRightGazeY - RightGazeY);
        if (rightDist > 0.006 || Math.Abs(_rightGazeVx) > 0.02 || Math.Abs(_rightGazeVy) > 0.02)
        {
            var fxR = kGaze * (_targetRightGazeX - RightGazeX) - dGaze * _rightGazeVx;
            var fyR = kGaze * (_targetRightGazeY - RightGazeY) - dGaze * _rightGazeVy;
            _rightGazeVx += fxR * dt;
            _rightGazeVy += fyR * dt;
            RightGazeX += _rightGazeVx * dt;
            RightGazeY += _rightGazeVy * dt;
            active = true;
        }
        else
        {
            RightGazeX = _targetRightGazeX;
            RightGazeY = _targetRightGazeY;
            _rightGazeVx = 0;
            _rightGazeVy = 0;
        }

        // 4. Tilt Angle (Decays to 0 so the blob body stays completely still during mouse tracking)
        if (Math.Abs(TiltAngle) > 0.01 || Math.Abs(_tiltVelocity) > 0.01)
        {
            const double kTilt = 220.0, dTilt = 18.0;
            var fTilt = -kTilt * TiltAngle - dTilt * _tiltVelocity;
            _tiltVelocity += fTilt * dt;
            TiltAngle += _tiltVelocity * dt;
            active = true;
        }
        else
        {
            TiltAngle = 0;
            _tiltVelocity = 0;
        }

        // 5. Autonomous Blinking & Winking (Strictly on eyes)
        if (!_isBlinking)
        {
            _blinkTimer -= dt;
            if (_blinkTimer <= 0)
            {
                _isBlinking = true;
                _blinkElapsed = 0;
                var roll = Random.Shared.NextDouble();
                if (roll < 0.15)
                {
                    _isWinking = true;
                    _winkLeftEye = roll < 0.075;
                }
                else
                {
                    _isWinking = false;
                }
            }
        }

        if (_isBlinking)
        {
            const double blinkDuration = 0.13;
            _blinkElapsed += dt;
            var u = Math.Clamp(_blinkElapsed / blinkDuration, 0.0, 1.0);
            var rawScale = u < 0.5
                ? 1.0 - (1.0 - 0.06) * (u / 0.5)
                : 0.06 + (1.0 - 0.06) * ((u - 0.5) / 0.5);

            if (_isWinking)
            {
                if (_winkLeftEye)
                {
                    LeftBlinkScaleY = rawScale;
                    RightBlinkScaleY = 1.0;
                }
                else
                {
                    LeftBlinkScaleY = 1.0;
                    RightBlinkScaleY = rawScale;
                }
            }
            else
            {
                LeftBlinkScaleY = rawScale;
                RightBlinkScaleY = rawScale;
            }

            if (u >= 1.0)
            {
                LeftBlinkScaleY = 1.0;
                RightBlinkScaleY = 1.0;
                if (_consecutiveBlinks > 0)
                {
                    _consecutiveBlinks--;
                    _isWinking = false;
                    _blinkElapsed = 0;
                }
                else
                {
                    _isBlinking = false;
                    _isWinking = false;
                    _blinkTimer = 3.2 + Random.Shared.NextDouble() * 2.4;
                }
            }
            active = true;
        }
        else
        {
            LeftBlinkScaleY = 1.0;
            RightBlinkScaleY = 1.0;
        }

        // 6. Happy Expression Decay
        if (_happyTimer > 0)
        {
            _happyTimer -= dt;
            if (_happyTimer <= 0)
                _targetHappy = 0.0;
        }
        if (Math.Abs(_targetHappy - HappyCurve) > 0.01)
        {
            HappyCurve += (_targetHappy - HappyCurve) * Math.Min(1.0, 12.0 * dt);
            active = true;
        }
        else
        {
            HappyCurve = _targetHappy;
        }

        // 7. Interactive Squish Bounce (Active only on direct tap/click, recovers to exactly 1.0)
        var squashOffset = _squash - 1.0;
        if (Math.Abs(squashOffset) > 0.002 || Math.Abs(_squashVelocity) > 0.01)
        {
            const double kSquash = 420.0, dSquash = 20.0;
            var force = -kSquash * squashOffset - dSquash * _squashVelocity;
            _squashVelocity += force * dt;
            _squash += _squashVelocity * dt;
            ScaleY = Math.Max(0.2, _squash);
            ScaleX = Math.Max(0.2, 2.0 - _squash);
            active = true;
        }
        else
        {
            _squash = 1.0;
            _squashVelocity = 0;
            ScaleX = 1.0;
            ScaleY = 1.0;
        }

        FloatY = 0;

        return active;
    }
}

/// <summary>
/// Hardware-accelerated GPU renderer for the Blobatar owl character.
/// Renders an anchored, still body blob with responsive, independently gliding eyes.
/// Features natural directional gaze: expressive angular attitude tilt, 3D spherical
/// head perspective foreshortening, and dynamic specular catchlights.
/// </summary>
public static class BlobatarRenderer
{
    // Exact mathematical vector paths generated from seed "owl" with traits:
    // eye.rx: 0.617, eye.ratio: 0.005, eye.n: 0, eye.gap: 0.594, hue: 0.8
    private static readonly Geometry BodyGeometry = Geometry.Parse(
        "M85.02 51.18C85.02 71.96 72.08 84.14 50.01 84.14C27.94 84.14 15.01 71.96 15.01 51.18C15.01 30.39 27.94 18.21 50.01 18.21C72.08 18.21 85.02 30.39 85.02 51.18Z");

    private static readonly Geometry LeftEyeGeometry = Geometry.Parse(
        "M45.05 44.46C45.7 49.75 45.34 50.71 42.56 51.05C39.79 51.39 39.2 50.54 38.56 45.25C37.91 39.96 38.27 39 41.05 38.66C43.82 38.32 44.41 39.17 45.05 44.46Z");

    private static readonly Geometry RightEyeGeometry = Geometry.Parse(
        "M65.89 45.33C66.5 49.15 66.22 49.86 63.98 50.21C61.74 50.56 61.26 49.97 60.66 46.16C60.06 42.35 60.33 41.64 62.57 41.28C64.81 40.93 65.29 41.52 65.89 45.33Z");

    public static readonly Color EyeColor = Color.FromRgb(0x0F, 0x0E, 0x18);

    public static Color ColorForTone(BlobatarTone tone) => tone switch
    {
        BlobatarTone.Idle => Color.FromRgb(0x84, 0x71, 0xE2),         // 0.71
        BlobatarTone.Alert => Color.FromRgb(0xA4, 0x99, 0xF7),        // 0.49
        BlobatarTone.Anticipation => Color.FromRgb(0xCE, 0xCA, 0xFF), // 0.10
        BlobatarTone.Busy => Color.FromRgb(0xD6, 0xD5, 0xE9),         // 0.28
        BlobatarTone.Celebration => Color.FromRgb(0xD1, 0xCE, 0xFF),  // 0.865
        _ => Color.FromRgb(0x84, 0x71, 0xE2)
    };

    public static void Render(DrawingContext context, Rect bounds, BlobatarState state)
    {
        var size = Math.Min(bounds.Width, bounds.Height);
        // Tight-fit 70.0 unit coordinate space: fills the allocated bounding box edge-to-edge
        var scale = size / 70.0;
        if (scale <= 0.001)
            return;

        var cx = bounds.Center.X;
        var cy = bounds.Center.Y;

        // Visual centroid of the owl body is at (50, 52.5)
        var rootMatrix = Matrix.CreateTranslation(-50, -52.5) *
                         Matrix.CreateScale(scale, scale) *
                         Matrix.CreateTranslation(cx, cy);

        using (context.PushTransform(rootMatrix))
        {
            // 1. Ambient Contact Shadow (grounded under body base)
            var shadowBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
            context.DrawEllipse(shadowBrush, null, new Point(50, 84.5), 23 * state.ScaleX, 4.5);

            // 2. Blob Body: Grounded at base (Y = 84.14).
            // Stays completely still and anchored around all sides while tracking mouse!
            Matrix bodyMatrix;
            if (state.TiltAngle != 0)
            {
                bodyMatrix = Matrix.CreateTranslation(-50, -84.14) *
                             Matrix.CreateRotation(state.TiltAngle * Math.PI / 180.0) *
                             Matrix.CreateScale(state.ScaleX, state.ScaleY) *
                             Matrix.CreateTranslation(50, 84.14);
            }
            else if (state.ScaleX != 1.0 || state.ScaleY != 1.0)
            {
                bodyMatrix = Matrix.CreateTranslation(-50, -84.14) *
                             Matrix.CreateScale(state.ScaleX, state.ScaleY) *
                             Matrix.CreateTranslation(50, 84.14);
            }
            else
            {
                bodyMatrix = Matrix.Identity;
            }

            using (context.PushTransform(bodyMatrix))
            {
                // Body Fill: 3D directional illumination matching liquid glass theme
                var topColor = Lighten(state.CurrentColor, 0.14);
                var bottomColor = Darken(state.CurrentColor, 0.18);
                var bodyBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
                    GradientStops =
                    [
                        new GradientStop(topColor, 0.0),
                        new GradientStop(state.CurrentColor, 0.45),
                        new GradientStop(bottomColor, 1.0)
                    ]
                };
                context.DrawGeometry(bodyBrush, null, BodyGeometry);

                // Top rim specular edge highlight
                var rimBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.5, 0.18, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5, 0.45, RelativeUnit.Relative),
                    GradientStops =
                    [
                        new GradientStop(Color.FromArgb(115, 255, 255, 255), 0.0),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0)
                    ]
                };
                var rimPen = new Pen(rimBrush, 1.15);
                context.DrawGeometry(null, rimPen, BodyGeometry);

                // 3. EYES: Natural Directional Gaze with 3D Spherical Head Projection
                var eyeBrush = new SolidColorBrush(EyeColor);
                var glintBrush = Brushes.White;

                // --- LEFT EYE ---
                // Left socket center: (41.8, 44.8). Max bounds: (4.8, 4.0)
                var lx = state.LeftGazeX;
                var ly = state.LeftGazeY;
                var leftNormX = Math.Clamp(lx / 4.8, -1.0, 1.0);
                var leftNormY = Math.Clamp(ly / 4.0, -1.0, 1.0);

                // Expressive Directional Orientation Tilt (degrees):
                // Oval tilts towards gaze vector rather than sliding like a flat pillar
                var leftTiltDeg = state.LeftEyeTiltDeg;

                // 3D Spherical Perspective Foreshortening:
                // When looking right (nx > 0), left eye is trailing and compresses horizontally
                // When looking left (nx < 0), left eye is leading and preserves width
                var leftForeshortenX = state.LeftForeshortenX;
                var leftPitchForeshortenY = leftNormY < 0 ? (1.0 - (-leftNormY) * 0.08) : (1.0 - leftNormY * 0.05);

                var leftBlinkY = Math.Max(0.06, state.LeftBlinkScaleY * (1.0 - state.HappyCurve * 0.65));
                var leftScaleY = leftBlinkY * leftPitchForeshortenY;

                // Coordinated Eye Transform:
                // 1. Center at left socket pivot (41.8, 44.8)
                // 2. Perspective foreshortening (scaleX, scaleY)
                // 3. Directional attitude tilt (rotation)
                // 4. Translate by gaze excursion vector
                var leftEyePos = new Point(41.8 + lx, 44.8 + ly);
                var leftEyeMatrix = Matrix.CreateTranslation(-41.8, -44.8) *
                                    Matrix.CreateScale(leftForeshortenX, leftScaleY) *
                                    Matrix.CreateRotation(leftTiltDeg * Math.PI / 180.0) *
                                    Matrix.CreateTranslation(leftEyePos.X, leftEyePos.Y);

                using (context.PushTransform(leftEyeMatrix))
                {
                    context.DrawGeometry(eyeBrush, null, LeftEyeGeometry);
                }

                // 3D Specular Catchlight (Corneal Highlight):
                // Drawn in head space so it stays oriented towards ambient light source (top-right),
                // shifting dynamically opposite to gaze to produce genuine 3D corneal curvature!
                if (leftBlinkY > 0.40)
                {
                    var glintRadius = state.HappyCurve > 0.4 ? 1.1 : 0.85;
                    var glintX = leftEyePos.X + 1.2 - leftNormX * 0.85;
                    var glintY = leftEyePos.Y - 2.0 - leftNormY * 0.55;
                    context.DrawEllipse(glintBrush, null, new Point(glintX, glintY), glintRadius, glintRadius);
                }

                // --- RIGHT EYE ---
                // Right socket center: (63.3, 45.7). Max bounds: (4.6, 3.8)
                var rx = state.RightGazeX;
                var ry = state.RightGazeY;
                var rightNormX = Math.Clamp(rx / 4.6, -1.0, 1.0);
                var rightNormY = Math.Clamp(ry / 3.8, -1.0, 1.0);

                // Expressive Directional Orientation Tilt (degrees):
                var rightTiltDeg = state.RightEyeTiltDeg;

                // 3D Spherical Perspective Foreshortening:
                // When looking left (nx < 0), right eye is trailing and compresses horizontally
                // When looking right (nx > 0), right eye is leading and preserves width
                var rightForeshortenX = state.RightForeshortenX;
                var rightPitchForeshortenY = rightNormY < 0 ? (1.0 - (-rightNormY) * 0.08) : (1.0 - rightNormY * 0.05);

                var rightBlinkY = Math.Max(0.06, state.RightBlinkScaleY * (1.0 - state.HappyCurve * 0.65));
                var rightScaleY = rightBlinkY * rightPitchForeshortenY;

                var rightEyePos = new Point(63.3 + rx, 45.7 + ry);
                var rightEyeMatrix = Matrix.CreateTranslation(-63.3, -45.7) *
                                     Matrix.CreateScale(rightForeshortenX, rightScaleY) *
                                     Matrix.CreateRotation(rightTiltDeg * Math.PI / 180.0) *
                                     Matrix.CreateTranslation(rightEyePos.X, rightEyePos.Y);

                using (context.PushTransform(rightEyeMatrix))
                {
                    context.DrawGeometry(eyeBrush, null, RightEyeGeometry);
                }

                if (rightBlinkY > 0.40)
                {
                    var glintRadius = state.HappyCurve > 0.4 ? 1.0 : 0.75;
                    var glintX = rightEyePos.X + 1.0 - rightNormX * 0.80;
                    var glintY = rightEyePos.Y - 1.6 - rightNormY * 0.50;
                    context.DrawEllipse(glintBrush, null, new Point(glintX, glintY), glintRadius, glintRadius);
                }
            }
        }
    }

    private static Color Lighten(Color c, double amount)
    {
        var r = (byte)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255);
        var g = (byte)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255);
        var b = (byte)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255);
        return Color.FromArgb(c.A, r, g, b);
    }

    private static Color Darken(Color c, double amount)
    {
        var r = (byte)Math.Clamp(c.R * (1.0 - amount), 0, 255);
        var g = (byte)Math.Clamp(c.G * (1.0 - amount), 0, 255);
        var b = (byte)Math.Clamp(c.B * (1.0 - amount), 0, 255);
        return Color.FromArgb(c.A, r, g, b);
    }
}
