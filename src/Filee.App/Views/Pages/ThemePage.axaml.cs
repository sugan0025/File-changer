using Avalonia.Controls;
using Avalonia.Interactivity;
using Filee.App.Services;

namespace Filee.App.Views.Pages;

public partial class ThemePage : UserControl
{
    public ThemePage()
    {
        InitializeComponent();
        PreviewPrimaryButton.Click += (_, _) => AppHost.TryGet<SoundService>()?.PlayConfirm();
        PreviewSecondaryButton.Click += (_, _) => AppHost.TryGet<SoundService>()?.PlayCancel();
        PreviewToggle.IsCheckedChanged += (_, _) =>
        {
            if (IsLoaded)
                AppHost.TryGet<SoundService>()?.PlayToggle(PreviewToggle.IsChecked == true);
        };
    }

    /// <summary>Plays the donut's open animation so the "reduce animations" setting can be felt right away.</summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        PreviewDonut.PlayOpenAnimation();
        AppHost.TryGet<SoundService>()?.PlayFanOut();
    }
}
