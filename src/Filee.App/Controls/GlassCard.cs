using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Filee.App.Services;

namespace Filee.App.Controls;

/// <summary>
/// Frosted Liquid Glass Card.
/// Subclasses Decorator with StyleKeyOverride = typeof(Border) so all existing
/// Avalonia Border selectors (Border.liquid-glass-master, Border.liquid-console-bar,
/// Border.liquid-glass-subcard, Border.card, etc.) match directly.
/// Automatically blits a localized, blurred slice of the backdrop wallpaper
/// behind its surface before rendering its dynamic glass gradient, specular borders, and children.
/// Repaints cleanly when scrolled in a ScrollViewer or resized.
/// </summary>
public class GlassCard : Decorator
{
    protected override Type StyleKeyOverride => typeof(Border);

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<GlassCard>();

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<GlassCard>();

    public static readonly StyledProperty<Thickness> BorderThicknessProperty =
        Border.BorderThicknessProperty.AddOwner<GlassCard>();

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<GlassCard>();

    public static readonly StyledProperty<BoxShadows> BoxShadowProperty =
        Border.BoxShadowProperty.AddOwner<GlassCard>();

    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public Thickness BorderThickness { get => GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }
    public CornerRadius CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public BoxShadows BoxShadow { get => GetValue(BoxShadowProperty); set => SetValue(BoxShadowProperty, value); }

    private ScrollViewer? _scroller;

    static GlassCard()
    {
        AffectsRender<GlassCard>(BoundsProperty, BackgroundProperty, BorderBrushProperty, BorderThicknessProperty, CornerRadiusProperty, BoxShadowProperty);
        AffectsMeasure<GlassCard>(BorderThicknessProperty, PaddingProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroller = this.FindAncestorOfType<ScrollViewer>();
        if (_scroller != null)
        {
            _scroller.ScrollChanged += OnScrollerChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_scroller != null)
        {
            _scroller.ScrollChanged -= OnScrollerChanged;
            _scroller = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScrollerChanged(object? sender, ScrollChangedEventArgs e)
    {
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var padding = Padding + BorderThickness;
        if (Child != null)
        {
            Child.Measure(constraint.Deflate(padding));
            return Child.DesiredSize.Inflate(padding);
        }
        return new Size(padding.Left + padding.Right, padding.Top + padding.Bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var padding = Padding + BorderThickness;
        Child?.Arrange(new Rect(finalSize).Deflate(padding));
        return finalSize;
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        var rect = new Rect(size);
        var r = CornerRadius;
        var roundedRect = new RoundedRect(rect, r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft);

        // 1. Localized blurred wallpaper backdrop slice
        if (BackdropService.Blurred is { } img &&
            TopLevel.GetTopLevel(this) is { } top &&
            top.Bounds.Width > 0 && top.Bounds.Height > 0 &&
            this.TranslatePoint(default, top) is { } offset)
        {
            var srcRect = BackdropService.CalculateSlice(offset, size, top.Bounds.Size, img.Size);
            using (ctx.PushClip(roundedRect))
            {
                ctx.DrawImage(img, srcRect, rect);
            }
        }

        // 2. Liquid glass background, specular border, and elevation box shadows
        var pen = BorderBrush != null && BorderThickness != default
            ? new Pen(BorderBrush, BorderThickness.Left)
            : null;

        ctx.DrawRectangle(Background, pen, roundedRect, BoxShadow);

        base.Render(ctx);
    }
}
