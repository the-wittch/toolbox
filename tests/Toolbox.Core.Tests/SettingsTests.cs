using System.Globalization;
using System.Text.Json.Nodes;
using Toolbox.Configuration;
using Toolbox.Models;

namespace Toolbox.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _install;
    private readonly string _user;

    public SettingsStoreTests()
    {
        _install = _temp.Combine("install", "appsettings.json");
        _user = _temp.Combine("user", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_install)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_user)!);
    }

    public void Dispose() => _temp.Dispose();

    private SettingsStore Store() => new(_install, _user);

    [Fact]
    public void Missing_install_file_uses_defaults_and_is_not_created()
    {
        var result = Store().Load();
        Assert.Equal("Toolbox", result.Settings.ProgramTitle);
        Assert.Single(result.Warnings);
        Assert.False(File.Exists(_install));
    }

    [Fact]
    public void Malformed_install_file_fails_closed_with_line_number()
    {
        File.WriteAllText(_install, "{\n  \"accessControl\": { \"enabled\": true,\n  oops\n}");
        var ex = Assert.Throws<SettingsLoadException>(() => Store().Load());
        Assert.Contains("line", ex.Message, StringComparison.Ordinal);
        Assert.Equal(_install, ex.FilePath);
    }

    [Fact]
    public void Install_file_keys_are_case_insensitive_and_comments_allowed()
    {
        File.WriteAllText(_install, "{ // site config\n \"ProgramTitle\": \"Desk\", \"LDAP\": { \"RootOu\": \"OU=X,DC=a,DC=b\" }, }");
        var settings = Store().Load().Settings;
        Assert.Equal("Desk", settings.ProgramTitle);
        Assert.Equal("OU=X,DC=a,DC=b", settings.Ldap.RootOu);
    }

    [Fact]
    public void Null_sections_are_replaced_with_defaults()
    {
        File.WriteAllText(_install, "{ \"ldap\": null, \"remoteTools\": null, \"search\": { \"presets\": null } }");
        var settings = Store().Load().Settings;
        Assert.NotNull(settings.Ldap);
        Assert.NotNull(settings.RemoteTools);
        Assert.NotNull(settings.Search.Presets);
    }

    [Fact]
    public void Overlay_only_overrides_keys_it_contains()
    {
        File.WriteAllText(_install, "{ \"ping\": { \"timeoutMs\": 900 }, \"theme\": \"Dark\" }");
        File.WriteAllText(_user, "{ \"theme\": \"Light\" }");
        var settings = Store().Load().Settings;
        Assert.Equal("Light", settings.Theme);
        Assert.Equal(900, settings.Ping.TimeoutMs);
    }

    [Fact]
    public void Blank_overlay_values_fall_back_to_install_values()
    {
        File.WriteAllText(_install, "{ \"programTitle\": \"Desk\", \"search\": { \"computerNamePrefixes\": [\"PC-\"] } }");
        File.WriteAllText(_user, "{ \"programTitle\": \"  \", \"search\": { \"computerNamePrefixes\": [] } }");
        var settings = Store().Load().Settings;
        Assert.Equal("Desk", settings.ProgramTitle);
        Assert.Equal(new[] { "PC-" }, settings.Search.ComputerNamePrefixes);
    }

    [Fact]
    public void Overlay_cannot_disable_access_control_or_update_trust()
    {
        File.WriteAllText(_install, """
            { "accessControl": { "enabled": true, "allowedGroups": ["CORP\\HelpDesk"] },
              "deployment": { "trustedSignerThumbprints": ["AA"], "requireSignedUpdates": true } }
            """);
        File.WriteAllText(_user, """
            { "accessControl": { "enabled": false, "allowedGroups": ["Everyone"] },
              "deployment": { "trustedSignerThumbprints": [], "requireSignedUpdates": false, "allowDowngrade": true, "updateSourcePath": "\\\\srv\\share" } }
            """);
        var settings = Store().Load().Settings;
        Assert.True(settings.AccessControl.Enabled);
        Assert.Equal(new[] { @"CORP\HelpDesk" }, settings.AccessControl.AllowedGroups);
        Assert.True(settings.Deployment.RequireSignedUpdates);
        Assert.False(settings.Deployment.AllowDowngrade);
        Assert.Equal(@"\\srv\share", settings.Deployment.UpdateSourcePath);
    }

    [Fact]
    public void Malformed_overlay_is_quarantined_and_install_values_used()
    {
        File.WriteAllText(_install, "{ \"programTitle\": \"Desk\" }");
        File.WriteAllText(_user, "{ not json");
        var result = Store().Load();
        Assert.Equal("Desk", result.Settings.ProgramTitle);
        Assert.False(File.Exists(_user));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_user)!, "appsettings.json.corrupt-*"));
        Assert.Contains(result.Warnings, w => w.Contains("invalid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Overlay_with_wrong_types_is_quarantined()
    {
        File.WriteAllText(_install, "{}");
        File.WriteAllText(_user, "{ \"ping\": { \"timeoutMs\": \"fast\" } }");
        var result = Store().Load();
        Assert.Equal(3000, result.Settings.Ping.TimeoutMs);
        Assert.False(File.Exists(_user));
    }

    [Fact]
    public void SaveUser_writes_only_differences_and_never_touches_install_file()
    {
        const string installJson = "{ \"programTitle\": \"Desk\", \"ping\": { \"timeoutMs\": 900 } }";
        File.WriteAllText(_install, installJson);
        var store = Store();
        var settings = store.Load().Settings;
        settings.Theme = "Dark";
        settings.SetupCompleted = true;
        settings.AccessControl.Enabled = true;
        store.SaveUser(settings);

        Assert.Equal(installJson, File.ReadAllText(_install));
        var overlay = JsonNode.Parse(File.ReadAllText(_user))!.AsObject();
        Assert.Equal("Dark", (string?)overlay["theme"]);
        Assert.True((bool?)overlay["setupCompleted"]);
        Assert.False(overlay.ContainsKey("ping"));
        Assert.False(overlay.ContainsKey("programTitle"));
        Assert.False(overlay.ContainsKey("accessControl"));
    }

    [Fact]
    public void Install_changes_reach_users_after_they_saved_settings()
    {
        File.WriteAllText(_install, "{ \"ping\": { \"timeoutMs\": 900 } }");
        var store = Store();
        var settings = store.Load().Settings;
        settings.Theme = "Dark";
        store.SaveUser(settings);

        File.WriteAllText(_install, "{ \"ping\": { \"timeoutMs\": 1500 } }");
        var reloaded = Store().Load().Settings;
        Assert.Equal(1500, reloaded.Ping.TimeoutMs);
        Assert.Equal("Dark", reloaded.Theme);
    }

    [Fact]
    public void ResetUser_moves_overlay_aside()
    {
        File.WriteAllText(_install, "{}");
        File.WriteAllText(_user, "{ \"theme\": \"Dark\" }");
        var store = Store();
        store.ResetUser();
        Assert.False(File.Exists(_user));
        Assert.True(File.Exists(_user + ".bak"));
        Assert.Equal("System", store.Load().Settings.Theme);
    }
}

public class SettingsJsonTests
{
    [Fact]
    public void Diff_of_identical_objects_is_empty()
    {
        var node = SettingsJson.ToCanonicalNode(new AppSettings());
        Assert.Empty(SettingsJson.Diff(node, SettingsJson.ToCanonicalNode(new AppSettings())));
    }

    [Fact]
    public void Diff_then_merge_round_trips()
    {
        var baseline = new AppSettings();
        var changed = SettingsJson.Clone(baseline);
        changed.Ldap.RootOu = "OU=Y,DC=c,DC=d";
        changed.RemoteTools.Add(new RemoteToolDefinition { Id = "x", CandidatePaths = { @"C:\x.exe" } });
        var baseNode = SettingsJson.ToCanonicalNode(baseline);
        var diff = SettingsJson.Diff(baseNode, SettingsJson.ToCanonicalNode(changed));
        var merged = SettingsJson.Deserialize(SettingsJson.Merge(baseNode, diff));
        Assert.Equal("OU=Y,DC=c,DC=d", merged.Ldap.RootOu);
        Assert.Single(merged.RemoteTools);
    }
}

public class MachinePolicyTests
{
    [Fact]
    public void Policy_groups_enable_access_control()
    {
        var settings = new AppSettings();
        new MachinePolicy { AllowedGroups = new[] { " CORP\\HD ", "" } }.ApplyTo(settings);
        Assert.True(settings.AccessControl.Enabled);
        Assert.Equal(new[] { @"CORP\HD" }, settings.AccessControl.AllowedGroups);
    }

    [Fact]
    public void Policy_signers_require_signed_updates()
    {
        var settings = new AppSettings();
        new MachinePolicy { TrustedUpdateSigners = new[] { "AB" }, AutoUpdate = false, UpdateSourcePath = @"\\a\b" }.ApplyTo(settings);
        Assert.True(settings.Deployment.RequireSignedUpdates);
        Assert.False(settings.Deployment.AutoUpdateOnClose);
        Assert.Equal(@"\\a\b", settings.Deployment.UpdateSourcePath);
    }

    [Fact]
    public void Empty_policy_changes_nothing()
    {
        var settings = new AppSettings();
        var policy = new MachinePolicy();
        Assert.True(policy.IsEmpty);
        policy.ApplyTo(settings);
        Assert.False(settings.AccessControl.Enabled);
    }
}

public class PresetQueryTests
{
    private static readonly DateTime Now = new(2026, 3, 7, 10, 0, 0, DateTimeKind.Local);

    [Fact]
    public void Today_token_uses_format() =>
        Assert.Equal("3/7/2026", PresetQuery.Resolve("{today:M/d/yyyy}", Now, CultureInfo.InvariantCulture));

    [Fact]
    public void Today_token_without_format_uses_default() =>
        Assert.Equal("3/7/2026", PresetQuery.Resolve("{today:}", Now, CultureInfo.InvariantCulture));

    [Fact]
    public void Plain_text_passes_through() =>
        Assert.Equal("Login: ", PresetQuery.Resolve("Login: ", Now, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("OU=Computers,DC=example,DC=com", true)]
    [InlineData("", true)]
    [InlineData("OU=PCs,DC=contoso,DC=com", false)]
    public void Placeholder_detection(string ou, bool expected) =>
        Assert.Equal(expected, PresetQuery.LooksLikePlaceholderOu(ou));
}

public class SettingsValidatorTests
{
    private static AppSettings Valid()
    {
        var s = new AppSettings();
        s.Ldap.RootOu = "OU=PCs,DC=contoso,DC=com";
        s.RemoteTools.Add(new RemoteToolDefinition { Id = "rdp", Arguments = "/v:{computerName}", CandidatePaths = { "mstsc.exe" } });
        return s;
    }

    private static IEnumerable<ValidationIssue> Errors(AppSettings s) =>
        SettingsValidator.Validate(s).Where(i => i.Severity == IssueSeverity.Error);

    [Fact]
    public void Valid_settings_have_no_errors() => Assert.Empty(Errors(Valid()));

    [Fact]
    public void Shipped_appsettings_has_no_errors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.shipped.json");
        var settings = SettingsJson.Deserialize(SettingsJson.ParseObject(File.ReadAllText(path)));
        Assert.DoesNotContain(Errors(settings), i => i.Path != "ldap.rootOu");
    }

    [Fact]
    public void Access_control_without_groups_is_an_error()
    {
        var s = Valid();
        s.AccessControl.Enabled = true;
        Assert.Contains(Errors(s), i => i.Path == "accessControl.allowedGroups");
    }

    [Theory]
    [InlineData(@"tools\x.exe")]
    [InlineData(@"C:\tools\x.bat")]
    [InlineData(@"C:\tools\x.lnk")]
    [InlineData(@"\\{computerName}\c$\x.exe")]
    [InlineData(@"\\?\C:\x.exe")]
    public void Unsafe_tool_paths_are_errors(string path)
    {
        var s = Valid();
        s.RemoteTools[0].CandidatePaths[0] = path;
        Assert.Contains(Errors(s), i => i.Path.StartsWith("remoteTools[0].candidatePaths", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData(@"\\server\share\x.exe")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("https://user:pass@host/")]
    public void Unsafe_urls_are_errors(string url)
    {
        var s = Valid();
        s.Streaming.Endpoints.Add(new StreamingEndpoint { Id = "a", ViewStreamUrl = url });
        Assert.Contains(Errors(s), i => i.Path == "streaming.endpoints[0].viewStreamUrl");
    }

    [Fact]
    public void Require_signed_without_pins_is_an_error()
    {
        var s = Valid();
        s.Deployment.RequireSignedUpdates = true;
        Assert.Contains(Errors(s), i => i.Path == "deployment.requireSignedUpdates");
    }

    [Fact]
    public void Bad_thumbprint_is_an_error()
    {
        var s = Valid();
        s.Deployment.TrustedSignerThumbprints.Add("not-a-thumbprint");
        Assert.Contains(Errors(s), i => i.Path == "deployment.trustedSignerThumbprints[0]");
    }

    [Fact]
    public void Relative_update_source_is_an_error()
    {
        var s = Valid();
        s.Deployment.UpdateSourcePath = @"apps\Toolbox";
        Assert.Contains(Errors(s), i => i.Path == "deployment.updateSourcePath");
    }

    [Fact]
    public void Out_of_range_numbers_are_warnings_and_clamped()
    {
        var s = Valid();
        s.Ldap.PageSize = 0;
        Assert.Contains(SettingsValidator.Validate(s), i => i.Path == "ldap.pageSize" && i.Severity == IssueSeverity.Warning);
        Assert.Equal(1, SettingsLimits.PageSize(s.Ldap));
    }

    [Fact]
    public void Malformed_filter_is_an_error()
    {
        var s = Valid();
        s.Ldap.ComputerObjectFilter = "(objectCategory=computer";
        Assert.Contains(Errors(s), i => i.Path == "ldap.computerObjectFilter");
    }
}

public sealed class SearchHistoryStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Round_trips_and_deduplicates()
    {
        var store = new SearchHistoryStore(_temp.Combine("h.json"));
        store.Save(new[] { "a", "A", " b ", "", "c" }, 2);
        Assert.Equal(new[] { "a", "b" }, store.Load(10));
    }

    [Fact]
    public void Corrupt_file_loads_empty()
    {
        var path = _temp.Write("h.json", "{");
        Assert.Empty(new SearchHistoryStore(path).Load(10));
    }
}

public class StreamingSettingsCompatibilityTests
{
    [Fact]
    public void Change_password_url_keeps_its_json_name()
    {
        var settings = SettingsJson.Deserialize(SettingsJson.ParseObject("{ \"streaming\": { \"changePasswordUrl\": \"https://a.example/\" } }"));
        Assert.Equal("https://a.example/", settings.Streaming.AccountPageUrl);
        Assert.True(SettingsJson.ToCanonicalNode(settings)["streaming"]!.AsObject().ContainsKey("changePasswordUrl"));
    }
}
