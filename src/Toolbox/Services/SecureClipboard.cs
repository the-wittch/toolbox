using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Toolbox.Diagnostics;
using Toolbox.Security;
using Windows.ApplicationModel.DataTransfer;

namespace Toolbox.Services;

public sealed class SecureClipboard
{
    private readonly ClipboardSecretTracker _tracker = new();
    private readonly DispatcherQueueTimer _timer;

    public SecureClipboard(DispatcherQueue dispatcherQueue)
    {
        _timer = dispatcherQueue.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += async (_, _) => await ClearIfStillOursAsync();
    }

    public static void CopyText(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    // Kept out of clipboard history and cloud sync. Other clipboard managers may still see it.
    public bool CopySecret(string secret, TimeSpan clearAfter)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(secret);
        var options = new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false };

        bool stored;
        try
        {
            stored = Clipboard.SetContentWithOptions(package, options);
        }
        catch (COMException ex)
        {
            AppLog.Warn("Clipboard was busy.", ex);
            return false;
        }

        if (!stored)
        {
            return false;
        }

        _tracker.Track(secret, NativeMethods.GetClipboardSequenceNumber());
        _timer.Stop();
        if (clearAfter > TimeSpan.Zero)
        {
            _timer.Interval = clearAfter;
            _timer.Start();
        }

        return true;
    }

    public async Task ClearIfStillOursAsync()
    {
        _timer.Stop();
        if (!_tracker.IsTracking)
        {
            return;
        }

        try
        {
            if (_tracker.IsUnchangedSince(NativeMethods.GetClipboardSequenceNumber()))
            {
                Clipboard.Clear();
                return;
            }

            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text) && _tracker.Matches(await content.GetTextAsync()))
            {
                Clipboard.Clear();
            }
        }
        catch (COMException ex)
        {
            AppLog.Warn("Could not check the clipboard.", ex);
        }
        finally
        {
            _tracker.Reset();
        }
    }

    public void ClearOnExit()
    {
        _timer.Stop();
        try
        {
            if (_tracker.IsUnchangedSince(NativeMethods.GetClipboardSequenceNumber()))
            {
                Clipboard.Clear();
            }
        }
        catch (COMException ex)
        {
            AppLog.Warn("Could not clear the clipboard on exit.", ex);
        }
        finally
        {
            _tracker.Reset();
        }
    }
}
