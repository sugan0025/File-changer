using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Filee.App.Services;

namespace Filee.App.Controls;

/// <summary>
/// Interactive Blobatar mascot: window-wide gaze tracking, breathing, jelly squish, circadian lighting and sound.
/// Ambient motion keeps a ~30 fps loop alive while visible; the loop stops when hidden, minimised or animations are off.
/// </summary>
public class BlobatarControl : Control
{
    public static readonly StyledProperty<BlobatarTone> ToneProperty =
        AvaloniaProperty.Register<BlobatarControl, BlobatarTone>(nameof(Tone), BlobatarTone.Idle);

    public static readonly StyledProperty<bool> TrackWindowPointerProperty =
        AvaloniaProperty.Register<BlobatarControl, bool>(nameof(TrackWindowPointer), true);

    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<BlobatarControl, bool>(nameof(IsAnimated), true);

    public static readonly StyledProperty<bool> PointerReactionsEnabledProperty =
        AvaloniaProperty.Register<BlobatarControl, bool>(nameof(PointerReactionsEnabled), true);

    public static readonly StyledProperty<bool> AudioEnabledProperty =
        AvaloniaProperty.Register<BlobatarControl, bool>(nameof(AudioEnabled), true);

    public static readonly StyledProperty<int> MaxFramesPerSecondProperty =
        AvaloniaProperty.Register<BlobatarControl, int>(nameof(MaxFramesPerSecond), 0);

    public BlobatarTone Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public bool TrackWindowPointer { get => GetValue(TrackWindowPointerProperty); set => SetValue(TrackWindowPointerProperty, value); }
    public bool IsAnimated { get => GetValue(IsAnimatedProperty); set => SetValue(IsAnimatedProperty, value); }
    public bool PointerReactionsEnabled { get => GetValue(PointerReactionsEnabledProperty); set => SetValue(PointerReactionsEnabledProperty, value); }
    public bool AudioEnabled { get => GetValue(AudioEnabledProperty); set => SetValue(AudioEnabledProperty, value); }
    public int MaxFramesPerSecond { get => GetValue(MaxFramesPerSecondProperty); set => SetValue(MaxFramesPerSecondProperty, value); }
    private float _mouthOpen;
    public float MouthOpen => _mouthOpen;

    public BlobatarState State { get; } = new();

    private const double IdleFrameInterval = 1.0 / 30.0;

    private TopLevel? _topLevel;
    private CircadianService? _circadian;
    private bool _isLooping;
    private TimeSpan? _lastFrameTime;
    private bool _isHovered;
    private bool _pressed;
    private bool _quietToneChange;
    private double _sinceDraw;

    static BlobatarControl()
    {
        AffectsRender<BlobatarControl>(ToneProperty);
    }

    public BlobatarControl()
    {
        ClipToBounds = false;
    }

    // ----- Hooks for the host (not wired yet) -----

    public void SetThinking(bool thinking, double load = 0) { State.SetThinking(thinking, load); StartLoop(); }
    public void SetTone(BlobatarTone tone) { Tone = tone; State.SetTone(tone); StartLoop(); }
    public void SetToneQuiet(BlobatarTone tone)
    {
        _quietToneChange = true;
        try { SetCurrentValue(ToneProperty, tone); }
        finally { _quietToneChange = false; }
        State.SetToneQuiet(tone);
        StartLoop();
    }
    public void Celebrate() { State.Celebrate(); StartLoop(); }
    public void Proud() { State.Proud(); StartLoop(); }
    public void Startle(double stimulusDx = 0) { State.Startle(stimulusDx); StartLoop(); }
    public void NotifyDragOver(bool over) { State.SetDragOver(over); StartLoop(); }
    public void NotifyDrop() { State.SetDragOver(false); State.Drop(); StartLoop(); }
    public void NotifyCompanionDrop() { State.SetDragOver(false); State.CompanionDrop(); StartLoop(); }
    public void SetCurious(bool curious) { State.SetCurious(curious); StartLoop(); }
    public void SetPaused(bool paused) { State.SetPaused(paused); StartLoop(); }
    public void SetNapping(bool napping) { State.SetNapping(napping); StartLoop(); }
    public void SetMouthOpen(double amount) { _mouthOpen = (float)Math.Clamp(amount, 0.0, 1.0); StartLoop(); InvalidateVisual(); }
    public void TriggerBoredHop() { State.TriggerBoredHop(); StartLoop(); }
    public void CancelBoredHop() { State.CancelBoredHop(); StartLoop(); }
    public void TriggerStretch() { State.TriggerStretch(); StartLoop(); }
    public void PressForReaction() { State.Press(); StartLoop(); }
    public void ReleaseForReaction() { State.Release(); StartLoop(); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ToneProperty && !_isHovered)
        {
            if (_quietToneChange)
                State.SetToneQuiet(change.GetNewValue<BlobatarTone>());
            else
                State.SetTone(change.GetNewValue<BlobatarTone>());
            StartLoop();
        }
        else if (change.Property == AudioEnabledProperty)
        {
            State.AudioEnabled = change.GetNewValue<bool>();
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

        _circadian = AppHost.TryGet<CircadianService>();
        if (_circadian is not null)
            _circadian.PhaseChanged += OnCircadianChanged;

        State.AudioEnabled = AudioEnabled;
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
        if (_circadian is not null)
        {
            _circadian.PhaseChanged -= OnCircadianChanged;
            _circadian = null;
        }
        State.AudioEnabled = false;
        _isLooping = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnCircadianChanged(object? sender, EventArgs e) => StartLoop();

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!TrackWindowPointer || !IsVisible)
            return;

        var bounds = new Rect(Bounds.Size);
        var size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 1.0)
            return;

        var posInControl = e.GetPosition(this);
        var cursorInViewBox = BlobatarRenderer.ToViewBox(posInControl, bounds);

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
        if (!PointerReactionsEnabled)
            return;
        _isHovered = true;
        State.TriggerBounce(0.55);
        State.TriggerTiltWobble(9.0);
        State.SetTone(BlobatarTone.Alert);
        State.SetHoverEnergy(1.0);
        StartLoop();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!PointerReactionsEnabled)
            return;
        _isHovered = false;
        State.SetHoverEnergy(0.0);
        State.SetTone(Tone);
        ReleaseIfPressed();
        StartLoop();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!PointerReactionsEnabled)
            return;
        _pressed = true;
        State.Press();                                        // squash down (+ soft thump)
        State.TriggerWink(Random.Shared.NextDouble() > 0.5);  // playful wink
        State.SetHappy(1.0, 1.6);
        StartLoop();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!PointerReactionsEnabled)
            return;
        ReleaseIfPressed();
        StartLoop();
    }

    private void ReleaseIfPressed()
    {
        if (!_pressed)
            return;
        _pressed = false;
        State.Release();                                      // jelly stretch + squish sound
    }

    private void StartLoop()
    {
        if (_isLooping || !IsAnimated || _topLevel is null)
            return;

        _isLooping = true;
        _lastFrameTime = null;
        _sinceDraw = 0;
        _topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        if (!_isLooping || _topLevel is null)
            return;

        var dt = _lastFrameTime is { } last ? Math.Min((time - last).TotalSeconds, 0.05) : 1 / 60.0;
        _lastFrameTime = time;

        var motion = Motion.Enabled;
        var active = State.Update(dt, motionEnabled: motion);

        // Transient motion draws every frame; ambient-only motion draws at ~30 fps.
        _sinceDraw += dt;
        var frameInterval = MaxFramesPerSecond > 0 ? Math.Max(IdleFrameInterval, 1.0 / MaxFramesPerSecond) : IdleFrameInterval;
        if (MaxFramesPerSecond > 0 ? _sinceDraw >= frameInterval : active || _sinceDraw >= IdleFrameInterval)
        {
            InvalidateVisual();
            _sinceDraw = 0;
        }

        var minimised = _topLevel is Window { WindowState: WindowState.Minimized };
        if (IsAnimated && IsVisible && !minimised && (active || motion))
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
        BlobatarRenderer.Render(context, new Rect(Bounds.Size), State, _mouthOpen);
    }
}
