using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Toolbox.Services;

namespace Toolbox.Views;

public sealed partial class InstallLocallyDialog : ContentDialog
{
    public LocalInstallResult? InstallResult { get; private set; }

    public InstallLocallyDialog()
    {
        InitializeComponent();
        DestinationText.Text = LocalInstallService.DefaultLocalInstallDirectory;
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var deferral = args.GetDeferral();
        try
        {
            InstallResult = App.LocalInstall.Install(createDesktopShortcut: true);
            if (!InstallResult.Succeeded)
            {
                args.Cancel = true;
                ErrorText.Text = InstallResult.Message;
                ErrorText.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
