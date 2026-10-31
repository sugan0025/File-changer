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
/// State machine for a Blobatar character instance: tracks eye gaze pursuit,
/// autonomous blinking, breathing, squishy hover spring physics, and color tone transitions.
/// </summary>
public sealed class BlobatarState
{
    // Physics & Gaze
    public double GazeX { get; private set; }
    public double GazeY { get; private set; }
    private double _gazeVx;
    private double _gazeVy;
    private double _targetGazeX;
    private double _targetGazeY;

    // Blinking
    public double BlinkScaleY { get; private set; } = 1.0;
    private double _blinkTimer = 3.8;
    private double _blinkElapsed;
    private bool _isBlinking;
    private int _consecutiveBlinks;

    // Breathing & Squish spring
    public double ScaleX { get; private set; } = 1.0;
    public double ScaleY { get; private set; } = 1.0;
    private double _breathPhase;
    private double _squash = 1.0;
    private double _squashVelocity;

    // Color interpolation
    public Color CurrentColor { get; private set; } = BlobatarRenderer.ColorForTone(BlobatarTone.Idle);
    private Color _targetColor = BlobatarRenderer.ColorForTone(BlobatarTone.Idle);

    public void SetTone(BlobatarTone tone)
    {
        _targetColor = BlobatarRenderer.ColorForTone(tone);
    }

    public void TriggerBounce(double impulse = 0.42)
    {
        _squashVelocity += impulse;
    }

    public void TriggerBlink(int count = 1)
    {
        _isBlinking = true;
        _blinkElapsed = 0;
        _consecutiveBlinks = Math.Max(0, count - 1);
    }

    public void LookAt(Point? cursorRelativeToCenter, double sensitivity = 130.0)
    {
        if (cursorRelativeToCenter is not { } p)
        {
            _targetGazeX = 0;
            _targetGazeY = 0;
            return;
        }

        var dx = p.X;
        var dy = p.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 0.001)
        {
            _targetGazeX = 0;
            _targetGazeY = 0;
            return;
        }

        // Maximum travel in viewBox coordinate space (viewBox is 0 0 100 100)
        const double maxTravel = 4.8;
        var factor = Math.Tanh(dist / sensitivity);
        _targetGazeX = (dx / dist) * factor * maxTravel;
        _targetGazeY = (dy / dist) * factor * (maxTravel * 0.85); // slight vertical dampening
    }

    /// <summary>
    /// Steps all physics, blinking, breathing, and color transitions.
    /// Returns true if ongoing motion requires continuous repainting.
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

        // 2. Gaze pursuit spring (k=260, d=24)
        var gazeDist = Math.Abs(_targetGazeX - GazeX) + Math.Abs(_targetGazeY - GazeY);
        if (gazeDist > 0.005 || Math.Abs(_gazeVx) > 0.01 || Math.Abs(_gazeVy) > 0.01)
        {
            const double kGaze = 260.0, dGaze = 24.0;
            var fx = kGaze * (_targetGazeX - GazeX) - dGaze * _gazeVx;
            var fy = kGaze * (_targetGazeY - GazeY) - dGaze * _gazeVy;
            _gazeVx += fx * dt;
            _gazeVy += fy * dt;
            GazeX += _gazeVx * dt;
            GazeY += _gazeVy * dt;
            active = true;
        }
        else
        {
            GazeX = _targetGazeX;
            GazeY = _targetGazeY;
            _gazeVx = 0;
            _gazeVy = 0;
        }

        // 3. Autonomous blinking
        if (!_isBlinking)
        {
            _blinkTimer -= dt;
            if (_blinkTimer <= 0)
            {
                _isBlinking = true;
                _blinkElapsed = 0;
            }
        }

        if (_isBlinking)
        {
            const double blinkDuration = 0.13;
            _blinkElapsed += dt;
            var u = Math.Clamp(_blinkElapsed / blinkDuration, 0.0, 1.0);
            if (u < 0.5)
            {
                // Closing lid
                BlinkScaleY = 1.0 - (1.0 - 0.06) * (u / 0.5);
            }
            else
            {
                // Opening lid
                BlinkScaleY = 0.06 + (1.0 - 0.06) * ((u - 0.5) / 0.5);
            }

            if (u >= 1.0)
            {
                BlinkScaleY = 1.0;
                if (_consecutiveBlinks > 0)
                {
                    _consecutiveBlinks--;
                    _blinkElapsed = 0; // immediate quick second blink
                }
                else
                {
                    _isBlinking = false;
                    _blinkTimer = 3.2 + Random.Shared.NextDouble() * 2.4; // 3.2s - 5.6s
                }
            }
            active = true;
        }

        // 4. Squish spring (k=450, d=22)
        var squashOffset = _squash - 1.0;
        if (Math.Abs(squashOffset) > 0.002 || Math.Abs(_squashVelocity) > 0.01)
        {
            const double kSquash = 450.0, dSquash = 22.0;
            var force = -kSquash * squashOffset - dSquash * _squashVelocity;
            _squashVelocity += force * dt;
            _squash += _squashVelocity * dt;
            active = true;
        }
        else
        {
            _squash = 1.0;
            _squashVelocity = 0;
        }

        // 5. Idle breathing
        if (motionEnabled)
        {
            _breathPhase = (_breathPhase + dt * 2.4) % (Math.PI * 2);
            var breath = Math.Sin(_breathPhase) * 0.016;
            ScaleY = Math.Max(0.2, _squash * (1.0 + breath));
            ScaleX = Math.Max(0.2, (2.0 - _squash) * (1.0 - breath));
            active = true;
        }
        else
        {
            ScaleX = 1.0;
            ScaleY = 1.0;
        }

        return active;
    }
}

/// <summary>
/// Hardware-accelerated GPU renderer for the Blobatar owl character.
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
        var scale = size / 100.0;
        if (scale <= 0.001)
            return;

        var cx = bounds.Center.X;
        var cy = bounds.Center.Y;

        // ViewBox is (0 0 100 100). Center is at (50, 50).
        var rootMatrix = Matrix.CreateTranslation(-50, -50) *
                         Matrix.CreateScale(scale, scale) *
                         Matrix.CreateTranslation(cx, cy);

        using (context.PushTransform(rootMatrix))
        {
            // 1. Ambient Contact Shadow
            var shadowBrush = new SolidColorBrush(Color.FromArgb(55, 0, 0, 0));
            context.DrawEllipse(shadowBrush, null, new Point(50, 84.5), 23 * state.ScaleX, 4.5);

            // 2. Squash & Stretch Body (grounded at base Y = 84.14)
            var bodyMatrix = Matrix.CreateTranslation(-50, -84.14) *
                             Matrix.CreateScale(state.ScaleX, state.ScaleY) *
                             Matrix.CreateTranslation(50, 84.14);

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
                        new GradientStop(Color.FromArgb(100, 255, 255, 255), 0.0),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0)
                    ]
                };
                var rimPen = new Pen(rimBrush, 1.1);
                context.DrawGeometry(null, rimPen, BodyGeometry);

                // 3. Eyes with Gaze & Blink
                var eyeBrush = new SolidColorBrush(EyeColor);
                var glintBrush = Brushes.White;

                // Left Eye: pivot at (41.8, 44.8)
                var leftEyeMatrix = Matrix.CreateTranslation(-41.8, -44.8) *
                                    Matrix.CreateScale(1.0, state.BlinkScaleY) *
                                    Matrix.CreateTranslation(41.8 + state.GazeX, 44.8 + state.GazeY);
                using (context.PushTransform(leftEyeMatrix))
                {
                    context.DrawGeometry(eyeBrush, null, LeftEyeGeometry);
                    if (state.BlinkScaleY > 0.45)
                    {
                        // Glossy Catchlight glint
                        context.DrawEllipse(glintBrush, null, new Point(41.8 + 1.2, 44.8 - 2.0), 0.85, 0.85);
                    }
                }

                // Right Eye: pivot at (63.3, 45.7)
                var rightEyeMatrix = Matrix.CreateTranslation(-63.3, -45.7) *
                                     Matrix.CreateScale(1.0, state.BlinkScaleY) *
                                     Matrix.CreateTranslation(63.3 + state.GazeX, 45.7 + state.GazeY);
                using (context.PushTransform(rightEyeMatrix))
                {
                    context.DrawGeometry(eyeBrush, null, RightEyeGeometry);
                    if (state.BlinkScaleY > 0.45)
                    {
                        // Glossy Catchlight glint
                        context.DrawEllipse(glintBrush, null, new Point(63.3 + 1.0, 45.7 - 1.6), 0.75, 0.75);
                    }
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
