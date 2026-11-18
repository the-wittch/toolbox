using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Toolbox.Services;

namespace Toolbox.Views;

public sealed partial class InstallLocallyDialog : ContentDialog
{
    public InstallLocallyDialog()
    {
        InitializeComponent();
        DestinationText.Text = LocalInstallService.DefaultLocalInstallDirectory;
    }

    public LocalInstallResult? InstallResult { get; private set; }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        ErrorText.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = IsSecondaryButtonEnabled = false;
        InstallProgress.Value = 0;
        InstallProgress.Visibility = Visibility.Visible;

        try
        {
            var progress = new Progress<double>(value => InstallProgress.Value = value);
            InstallResult = await App.LocalInstall.InstallAsync(progress, CancellationToken.None);
            if (!InstallResult.Succeeded)
            {
                args.Cancel = true;
                ErrorText.Text = InstallResult.Message;
                ErrorText.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            InstallProgress.Visibility = Visibility.Collapsed;
            IsPrimaryButtonEnabled = IsSecondaryButtonEnabled = true;
            deferral.Complete();
        }
    }
}
