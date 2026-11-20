using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Toolbox.Services;

internal static class NativeMethods
{
    private const uint NcryptSilentFlag = 0x40;

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    [DllImport("ncrypt.dll", ExactSpelling = true)]
    private static extern int NCryptUnprotectSecret(
        out IntPtr descriptor,
        uint flags,
        byte[] protectedBlob,
        uint protectedBlobLength,
        IntPtr memPara,
        IntPtr hWnd,
        out IntPtr data,
        out uint dataLength);

    [DllImport("ncrypt.dll", ExactSpelling = true)]
    private static extern int NCryptCloseProtectionDescriptor(IntPtr descriptor);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    // DPAPI-NG decryption runs as the signed-in user; the DC decides whether that user may decrypt
    public static string UnprotectSecret(ReadOnlyMemory<byte> blob)
    {
        var input = blob.ToArray();
        var status = NCryptUnprotectSecret(out var descriptor, NcryptSilentFlag, input, (uint)input.Length,
            IntPtr.Zero, IntPtr.Zero, out var data, out var length);
        try
        {
            if (status != 0)
            {
                throw new Win32Exception(status);
            }

            return Marshal.PtrToStringUni(data, (int)(length / 2));
        }
        finally
        {
            if (data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[length], 0, data, (int)length);
                _ = LocalFree(data);
            }

            if (descriptor != IntPtr.Zero)
            {
                _ = NCryptCloseProtectionDescriptor(descriptor);
            }
        }
    }
}
