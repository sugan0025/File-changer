using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Filee.App.Services;

namespace Filee.App.Controls;

/// <summary>
/// Interactive native Avalonia control hosting the Blobatar mascot.
/// Features continuous window-wide mouse tracking gaze, autonomous blinking,
/// squishy hover spring physics, and multi-tone state reactivity.
/// </summary>
public class BlobatarControl : Control
{
    public static readonly StyledProperty<BlobatarTone> ToneProperty =
        AvaloniaProperty.Register<BlobatarControl, BlobatarTone>(nameof(Tone), BlobatarTone.Idle);

    public static readonly StyledProperty<bool> TrackWindowPointerProperty =
        AvaloniaProperty.Register<BlobatarControl, bool>(nameof(TrackWindowPointer), true);

    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<BlobatarControl, bool>(nameof(IsAnimated), true);

    public BlobatarTone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public bool TrackWindowPointer
    {
        get => GetValue(TrackWindowPointerProperty);
        set => SetValue(TrackWindowPointerProperty, value);
    }

    public bool IsAnimated
    {
        get => GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    public BlobatarState State { get; } = new();

    private TopLevel? _topLevel;
    private bool _isLooping;
    private TimeSpan? _lastFrameTime;
    private bool _isHovered;

    static BlobatarControl()
    {
        AffectsRender<BlobatarControl>(ToneProperty);
    }

    public BlobatarControl()
    {
        ClipToBounds = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ToneProperty && !_isHovered)
        {
            State.SetTone(change.GetNewValue<BlobatarTone>());
            StartLoop();
        }
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

        State.SetTone(Tone);
        StartLoop();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
        {
            _topLevel.PointerMoved -= OnWindowPointerMoved;
            _topLevel.PointerExited -= OnWindowPointerExited;
            _topLevel = null;
        }
        _isLooping = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!TrackWindowPointer || !IsVisible)
            return;

        var bounds = Bounds;
        var size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 1.0)
            return;

        var posInControl = e.GetPosition(this);
        var scale = size / 70.0;
        var cx = bounds.Center.X;
        var cy = bounds.Center.Y;

        // Visual centroid in viewBox is at (50.0, 52.5)
        var cursorInViewBox = new Point(
            50.0 + (posInControl.X - cx) / scale,
            52.5 + (posInControl.Y - cy) / scale
        );

        State.LookAtCursor(cursorInViewBox);
        StartLoop();
    }

    private void OnWindowPointerExited(object? sender, PointerEventArgs e)
    {
        if (!TrackWindowPointer)
            return;

        State.LookAt(null);
        StartLoop();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _isHovered = true;
        State.TriggerBounce(0.55);
        State.TriggerTiltWobble(9.0);
        State.SetTone(BlobatarTone.Alert);
        StartLoop();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isHovered = false;
        State.SetTone(Tone);
        StartLoop();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        State.TriggerBounce(0.85); // deep squish on poke
        State.TriggerWink(Random.Shared.NextDouble() > 0.5); // playful wink on tap
        State.SetHappy(1.0, 1.6); // happy expression!
        StartLoop();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        State.TriggerBounce(0.35); // bouncy spring pop recovery
        StartLoop();
    }

    private void StartLoop()
    {
        if (_isLooping || !IsAnimated || _topLevel is null)
            return;

        _isLooping = true;
        _lastFrameTime = null;
        _topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        if (!_isLooping || _topLevel is null)
            return;

        var dt = _lastFrameTime is { } last ? Math.Min((time - last).TotalSeconds, 0.05) : 1 / 60.0;
        _lastFrameTime = time;

        var active = State.Update(dt, motionEnabled: Motion.Enabled);
        InvalidateVisual();

        // Keep running frame loop while active motion or breathing is enabled
        if (active && IsAnimated && IsVisible)
        {
            _topLevel.RequestAnimationFrame(OnFrame);
        }
        else
        {
            _isLooping = false;
        }
    }

    public override void Render(DrawingContext context)
    {
        BlobatarRenderer.Render(context, Bounds, State);
    }
}
