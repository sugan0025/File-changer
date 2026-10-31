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
    private bool _isClosed;

    public ClipboardNoticeWindow() => InitializeComponent();

    public ClipboardNoticeWindow(string title, string description, Action onConvert) : this()
    {
        _onConvert = onConvert;
        TitleText.Text = title;
        DescriptionText.Text = description;
        Motion.Track(this);
        SizeChanged += (_, _) => PlaceBottomRight();
        Closed += (_, _) => _isClosed = true;

        // Auto-dismiss after 8 seconds safely
        DispatcherTimer.RunOnce(() =>
        {
            if (!_isClosed)
            {
                try
                {
                    Close();
                }
                catch
                {
                }
            }
        }, TimeSpan.FromSeconds(8));
    }

    private void OnConvert(object? sender, RoutedEventArgs e)
    {
        if (!_isClosed)
        {
            _isClosed = true;
            try { Close(); } catch { }
        }
        _onConvert?.Invoke();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        if (!_isClosed)
        {
            _isClosed = true;
            try { Close(); } catch { }
        }
    }

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
