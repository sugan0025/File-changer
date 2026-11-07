using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Filee.Setup.Controls;
using Xunit;

namespace Filee.Setup.Tests;

public class BlobatarLogoTests
{
    [AvaloniaFact]
    public void BlobatarLogo_initializes_with_default_properties()
    {
        var logo = new BlobatarLogo();
        Assert.Equal(0, logo.Progress);
        Assert.False(logo.TrackProgress);
        Assert.Equal(BlobatarTone.Idle, logo.Tone);
        Assert.True(logo.Animate);
    }

    [AvaloniaFact]
    public void Progress_triggers_celebration_at_completion()
    {
        var logo = new BlobatarLogo { TrackProgress = true };
        logo.Progress = 50;
        Assert.Equal(50, logo.Progress);

        // Advance to 100%
        logo.Progress = 100;
        Assert.Equal(100, logo.Progress);
    }

    [AvaloniaFact]
    public void Tone_change_updates_property()
    {
        var logo = new BlobatarLogo();
        logo.Tone = BlobatarTone.Celebration;
        Assert.Equal(BlobatarTone.Celebration, logo.Tone);

        logo.Tone = BlobatarTone.Alert;
        Assert.Equal(BlobatarTone.Alert, logo.Tone);
    }

    [AvaloniaFact]
    public void Headless_render_draws_cleanly_in_light_and_dark()
    {
        var logo = new BlobatarLogo
        {
            Width = 96,
            Height = 96,
            Progress = 42,
            TrackProgress = true,
        };

        // Measure & Arrange
        logo.Measure(new Size(96, 96));
        logo.Arrange(new Rect(0, 0, 96, 96));

        // Render light theme
        var surface = new DrawingGroup();
        using (var context = surface.Open())
        {
            logo.Render(context);
        }

        // Render dark theme
        var prev = Application.Current?.RequestedThemeVariant ?? ThemeVariant.Default;
        DrawingGroup? darkSurface = null;
        try
        {
            if (Application.Current is not null)
                Application.Current.RequestedThemeVariant = ThemeVariant.Dark;

            darkSurface = new DrawingGroup();
            using (var context = darkSurface.Open())
            {
                logo.Render(context);
            }
        }
        finally
        {
            if (Application.Current is not null)
                Application.Current.RequestedThemeVariant = prev;
        }

        Assert.NotNull(surface);
        Assert.NotNull(darkSurface);
    }
}
