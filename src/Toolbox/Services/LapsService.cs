using System.ComponentModel;
using System.DirectoryServices;
using System.Globalization;
using System.Runtime.InteropServices;
using Toolbox.Configuration;
using Toolbox.Diagnostics;
using Toolbox.Laps;
using Toolbox.Ldap;
using Toolbox.Models;

namespace Toolbox.Services;

public sealed class LapsService
{
    private readonly SettingsService _settings;

    public LapsService(SettingsService settings)
    {
        _settings = settings;
    }

    public async Task<LapsResult> RetrieveAsync(ComputerItem item, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        if (!settings.Laps.Enabled)
        {
            return LapsResult.Disabled();
        }

        if (string.IsNullOrWhiteSpace(item.DistinguishedName))
        {
            return LapsResult.Failed("The directory object for this computer is unknown. Search again.");
        }

        var work = Task.Run(() => Retrieve(item.DistinguishedName, settings.Ldap, settings.Laps), CancellationToken.None);
        LapsResult result;
        try
        {
            result = await work.WaitAsync(SettingsLimits.LapsTimeout(settings.Laps), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            result = LapsResult.Failed("The LAPS lookup timed out.");
        }

        AppLog.Info($"LAPS password requested for {item.Name}: {result.Status} ({LapsResult.Describe(result.Source)}).");
        return result;
    }

    private static LapsResult Retrieve(string distinguishedName, LdapSettings ldap, LapsSettings laps)
    {
        try
        {
            var path = LdapPath.ForObject(distinguishedName, ldap);
            using var entry = DirectoryConnection.Open(path, ldap);
            using var searcher = new DirectorySearcher(entry, "(objectClass=computer)", LapsAttributes.All, SearchScope.Base)
            {
                ServerTimeLimit = SettingsLimits.LapsTimeout(laps),
                ClientTimeout = SettingsLimits.LapsTimeout(laps)
            };

            var result = searcher.FindOne();
            if (result is null)
            {
                return LapsResult.NotAvailable();
            }

            var windowsExpiry = LapsParsing.ParseExpiration(First(result, LapsAttributes.WindowsExpiration));

            if (First(result, LapsAttributes.WindowsEncryptedPassword) is byte[] encrypted)
            {
                return DecryptWindowsLaps(encrypted, windowsExpiry);
            }

            if (First(result, LapsAttributes.WindowsPassword) is string clear && clear.Length > 0)
            {
                var payload = LapsParsing.ParsePayload(clear);
                return LapsResult.Retrieved(LapsSource.WindowsLaps, payload.Password, payload.AccountName, windowsExpiry);
            }

            if (First(result, LapsAttributes.LegacyPassword) is string legacy && legacy.Length > 0)
            {
                var expiry = LapsParsing.ParseExpiration(First(result, LapsAttributes.LegacyExpiration));
                return LapsResult.Retrieved(LapsSource.LegacyLaps, legacy, null, expiry);
            }

            return LapsResult.NotAvailable();
        }
        catch (FormatException ex)
        {
            return LapsResult.Failed(ex.Message);
        }
        catch (COMException ex)
        {
            AppLog.Warn("LAPS lookup failed.", ex);
            return LapsResult.Failed(ErrorTranslator.Describe(ex));
        }
    }

    private static LapsResult DecryptWindowsLaps(byte[] attribute, DateTimeOffset? expiry)
    {
        try
        {
            var json = NativeMethods.UnprotectSecret(LapsParsing.ExtractEncryptedBlob(attribute));
            var payload = LapsParsing.ParsePayload(json);
            return LapsResult.Retrieved(LapsSource.WindowsLapsEncrypted, payload.Password, payload.AccountName, expiry);
        }
        catch (FormatException ex)
        {
            return LapsResult.DecryptionFailed(ex.Message);
        }
        catch (Win32Exception ex)
        {
            AppLog.Warn("Windows LAPS decryption failed.", ex);
            return LapsResult.DecryptionFailed(
                "The Windows LAPS password is encrypted and could not be decrypted. You may not be an authorized " +
                "password decryptor for this computer (error 0x" + ex.NativeErrorCode.ToString("X8", CultureInfo.InvariantCulture) + ").");
        }
    }

    private static object? First(SearchResult result, string property) =>
        result.Properties.Contains(property) && result.Properties[property].Count > 0
            ? result.Properties[property][0]
            : null;
}
