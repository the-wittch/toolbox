using Microsoft.Win32;
using Toolbox.Configuration;
using Toolbox.Diagnostics;
using Toolbox.Models;

namespace Toolbox.Services;

public sealed class SettingsService
{
    private readonly SettingsStore _store;
    private AppSettings _beforePolicy = new();

    public SettingsService()
    {
        InstallSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        UserSettingsPath = Path.Combine(AppDataFolder, "appsettings.json");
        _store = new SettingsStore(InstallSettingsPath, UserSettingsPath);
    }

    public static string AppDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Toolbox");

    public string InstallSettingsPath { get; }

    public string UserSettingsPath { get; }

    public AppSettings Current { get; private set; } = new();

    public MachinePolicy Policy { get; private set; } = new();

    public IReadOnlyList<string> LoadWarnings { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<ValidationIssue> Issues { get; private set; } = Array.Empty<ValidationIssue>();

    public bool NeedsFirstRunSetup => !Current.SetupCompleted;

    public void Load()
    {
        var result = _store.Load();
        Policy = ReadPolicy();
        _beforePolicy = SettingsJson.Clone(result.Settings);
        Policy.ApplyTo(result.Settings);
        Current = result.Settings;
        LoadWarnings = result.Warnings;
        Issues = SettingsValidator.Validate(Current);
    }

    public void SaveUserSettings(AppSettings settings, bool markSetupComplete)
    {
        var toSave = SettingsJson.Clone(settings);
        if (markSetupComplete)
        {
            toSave.SetupCompleted = true;
        }

        // keep policy-enforced values out of the user's file
        if (!string.IsNullOrWhiteSpace(Policy.UpdateSourcePath))
        {
            toSave.Deployment.UpdateSourcePath = _beforePolicy.Deployment.UpdateSourcePath;
        }

        if (Policy.AutoUpdate is not null)
        {
            toSave.Deployment.AutoUpdateOnClose = _beforePolicy.Deployment.AutoUpdateOnClose;
        }

        _store.SaveUser(toSave);
        AppLog.Info($"Personal settings saved to {UserSettingsPath}.");
        ReloadAfterSave();
    }

    public void ResetUserSettings()
    {
        _store.ResetUser();
        AppLog.Info("Personal settings reset to install defaults.");
        ReloadAfterSave();
    }

    private void ReloadAfterSave()
    {
        try
        {
            Load();
        }
        catch (SettingsLoadException ex)
        {
            AppLog.Error("Install settings became unreadable; keeping the settings already loaded.", ex);
        }
    }

    private static MachinePolicy ReadPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MachinePolicy.RegistryKeyPath);
            if (key is null)
            {
                return new MachinePolicy();
            }

            return new MachinePolicy
            {
                AllowedGroups = ReadList(key, "AllowedGroups"),
                DenyMessage = key.GetValue("DenyMessage") as string,
                UpdateSourcePath = key.GetValue("UpdateSourcePath") as string,
                AutoUpdate = key.GetValue("AutoUpdate") is int flag ? flag != 0 : null,
                TrustedUpdateSigners = ReadList(key, "TrustedUpdateSigners")
            };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            AppLog.Error("Machine policy could not be read.", ex);
            return new MachinePolicy { ReadError = ex.Message };
        }
    }

    private static IReadOnlyList<string>? ReadList(RegistryKey key, string name) => key.GetValue(name) switch
    {
        string[] values => values,
        string value => value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        _ => null
    };
}
