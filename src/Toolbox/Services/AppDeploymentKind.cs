using System.Runtime.InteropServices;
using System.Text;

namespace Toolbox.Services;

/// <summary>
/// Detects whether this process is running as an MSIX/AppX package (SCCM/sideload)
/// versus the unpackaged xcopy / LocalAppData install.
/// </summary>
public static class AppDeploymentKind
{
    private static readonly Lazy<bool> Packaged = new(DetectPackaged);

    public static bool IsPackaged => Packaged.Value;

    /// <summary>
    /// Robocopy self-update and "install locally" only apply to unpackaged share installs.
    /// SCCM owns install/update for packaged deployments.
    /// </summary>
    public static bool SupportsFileSyncUpdates => !IsPackaged;

    public static bool SupportsLocalInstallOffer => !IsPackaged;

    private static bool DetectPackaged()
    {
        try
        {
            // GetCurrentPackageFullName returns ERROR_INSUFFICIENT_BUFFER (122) when packaged
            // (with a required length), or APPMODEL_ERROR_NO_PACKAGE (15700) when unpackaged.
            var length = 0;
            var hr = GetCurrentPackageFullName(ref length, null);
            if (hr == 122 && length > 0)
            {
                var sb = new StringBuilder(length);
                hr = GetCurrentPackageFullName(ref length, sb);
                return hr == 0 && sb.Length > 0;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);
}
