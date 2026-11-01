// Lenis-style smooth inertial scrolling for Avalonia ScrollViewer.
// Automatically syncs with hardware animation frames and respects Motion.Enabled.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Rendering;
using Filee.App.Services;

namespace Filee.App.Controls;

/// <summary>
/// A <see cref="ScrollViewer"/> providing buttery-smooth inertial scrolling on mouse wheel events.
/// Automatically disabled when reduced motion is preferred (<see cref="Motion.Enabled"/>).
/// </summary>
public class SmoothScrollViewer : ScrollViewer
{
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public static readonly StyledProperty<bool> SmoothScrollingEnabledProperty =
        AvaloniaProperty.Register<SmoothScrollViewer, bool>(nameof(SmoothScrollingEnabled), defaultValue: true);

    public bool SmoothScrollingEnabled
    {
        get => GetValue(SmoothScrollingEnabledProperty);
        set => SetValue(SmoothScrollingEnabledProperty, value);
    }

    private Vector _targetOffset;
    private bool _isAnimating;
    private long _lastTick;
    private TopLevel? _topLevel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _targetOffset = Offset;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAnimating = false;
        _topLevel = null;
    }

    /// <summary>Resets scroll position and cancels any active smooth animation.</summary>
    public void ResetScroll()
    {
        _isAnimating = false;
        Offset = new Vector(0, 0);
        _targetOffset = new Vector(0, 0);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!SmoothScrollingEnabled || !Motion.Enabled || e.Handled)
        {
            base.OnPointerWheelChanged(e);
            _targetOffset = Offset;
            return;
        }

        var deltaY = e.Delta.Y;
        var deltaX = e.Delta.X;

        // Precision touchpad gestures send small fractional deltas; let base handle for instant touch response
        if (Math.Abs(deltaY) > 0 && Math.Abs(deltaY) < 0.75)
        {
            base.OnPointerWheelChanged(e);
            _targetOffset = Offset;
            return;
        }

        var maxOffsetY = Math.Max(0, Extent.Height - Viewport.Height);
        var maxOffsetX = Math.Max(0, Extent.Width - Viewport.Width);

        // Vertical wheel scroll
        if (deltaY != 0 && maxOffsetY > 0)
        {
            var step = deltaY * 115.0;
            var newY = Math.Clamp(_targetOffset.Y - step, 0, maxOffsetY);
            _targetOffset = new Vector(_targetOffset.X, newY);
            StartAnimation();
            e.Handled = true;
            return;
        }

        // Horizontal wheel scroll
        if ((deltaX != 0 || deltaY != 0) && maxOffsetX > 0)
        {
            var delta = deltaX != 0 ? deltaX : deltaY;
            var step = delta * 115.0;
            var newX = Math.Clamp(_targetOffset.X - step, 0, maxOffsetX);
            _targetOffset = new Vector(newX, _targetOffset.Y);
            StartAnimation();
            e.Handled = true;
            return;
        }

        base.OnPointerWheelChanged(e);
        _targetOffset = Offset;
    }

    private void StartAnimation()
    {
        if (_isAnimating)
            return;

        _topLevel ??= TopLevel.GetTopLevel(this);
        if (_topLevel is null)
        {
            Offset = _targetOffset;
            return;
        }

        _isAnimating = true;
        _lastTick = Environment.TickCount64;
        _topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        if (!_isAnimating)
            return;

        var current = Offset;
        var diff = _targetOffset - current;

        // Snap when close enough
        if (Math.Abs(diff.X) < 0.35 && Math.Abs(diff.Y) < 0.35)
        {
            Offset = _targetOffset;
            _isAnimating = false;
            return;
        }

        var now = Environment.TickCount64;
        var dt = _lastTick > 0 ? Math.Clamp((now - _lastTick) / 1000.0, 0.001, 0.05) : 0.016;
        _lastTick = now;

        // Lenis exponential dampening formula: 1 - exp(-speed * dt)
        var factor = 1.0 - Math.Exp(-14.0 * dt);

        Offset = new Vector(current.X + diff.X * factor, current.Y + diff.Y * factor);

        _topLevel?.RequestAnimationFrame(OnFrame);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == OffsetProperty)
        {
            var newOffset = change.GetNewValue<Vector>();
            if (_isAnimating && (Math.Abs(newOffset.X - _targetOffset.X) > 250 || Math.Abs(newOffset.Y - _targetOffset.Y) > 250))
            {
                _isAnimating = false;
                _targetOffset = newOffset;
            }
            else if (!_isAnimating)
            {
                _targetOffset = newOffset;
            }
        }
    }
}

