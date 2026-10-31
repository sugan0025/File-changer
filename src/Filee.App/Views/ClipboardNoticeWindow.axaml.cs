using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Filee.App.Services;

namespace Filee.App.Views;

public partial class ClipboardNoticeWindow : Window
{
    private const int ScreenMargin = 16;
    private readonly Action? _onConvert;

    public ClipboardNoticeWindow() => InitializeComponent();

    public ClipboardNoticeWindow(string title, string description, Action onConvert) : this()
    {
        _onConvert = onConvert;
        TitleText.Text = title;
        DescriptionText.Text = description;
        Motion.Track(this);
        SizeChanged += (_, _) => PlaceBottomRight();
        // Auto-dismiss after 8 seconds
        DispatcherTimer.RunOnce(Close, TimeSpan.FromSeconds(8));
    }

    private void OnConvert(object? sender, RoutedEventArgs e)
    {
        Close();
        _onConvert?.Invoke();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void PlaceBottomRight()
    {
        var screen = Screens.Primary;
        if (screen is null)
            return;
        var area = screen.WorkingArea;
        var width = (int)Math.Ceiling(Bounds.Width * screen.Scaling);
        var height = (int)Math.Ceiling(Bounds.Height * screen.Scaling);
        Position = new PixelPoint(area.Right - width - ScreenMargin, area.Bottom - height - ScreenMargin);
    }
}

