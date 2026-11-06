using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Filee.App.Views;

/// <summary>A borderless, non-activating host for Filee's existing Blobatar control.</summary>
public partial class BlobCompanionWindow : Window
{
    public BlobCompanionWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
        PositionChanged += (_, e) => PositionMoved?.Invoke(this, e.Point);
    }

    public event EventHandler<PixelPoint>? PositionMoved;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            BeginMoveDrag(e);
        }
    }
}
