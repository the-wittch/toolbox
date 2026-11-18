namespace Toolbox.Services;

public static class AppDeploymentKind
{
    private const int ErrorInsufficientBuffer = 122;

    private static readonly Lazy<bool> Packaged = new(DetectPackaged);

    public static bool IsPackaged => Packaged.Value;

    public static bool SupportsFileSyncUpdates => !IsPackaged;

    public static bool SupportsLocalInstallOffer => !IsPackaged;

    private static bool DetectPackaged()
    {
        try
        {
            var length = 0;
            var hr = NativeMethods.GetCurrentPackageFullName(ref length, null);
            if (hr != ErrorInsufficientBuffer || length <= 0)
            {
                return false;
            }

            var name = new char[length];
            return NativeMethods.GetCurrentPackageFullName(ref length, name) == 0 && length > 1;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
