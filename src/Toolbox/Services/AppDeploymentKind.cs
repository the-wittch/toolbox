using System.Runtime.InteropServices;
using System.Text;

namespace Toolbox.Services;

public static class AppDeploymentKind
{
    private static readonly Lazy<bool> Packaged = new(DetectPackaged);

    public static bool IsPackaged => Packaged.Value;

    public static bool SupportsFileSyncUpdates => !IsPackaged;

    public static bool SupportsLocalInstallOffer => !IsPackaged;

    private static bool DetectPackaged()
    {
        try
        {

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
