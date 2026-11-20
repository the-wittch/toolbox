# Troubleshooting

Start with the log: **Open log folder** in the status bar, or `%LocalAppData%\Toolbox\logs`. Each day has its own `toolbox-yyyyMMdd.log`. Fatal errors also get a `crash-*.log`, which includes a check of the files an unpackaged WinUI app needs.

| Symptom | What to check |
|---------|---------------|
| "Toolbox cannot start because its configuration is invalid" | The install `appsettings.json` has a JSON error; the message gives the line. Fix it on the share (then run `Update-ToolboxManifest.ps1`) |
| "Access denied" at start | User isn't in `accessControl.allowedGroups` or the `AllowedGroups` policy. Group changes need a new sign-in. The log has the reason, including group names that couldn't be resolved |
| Crash at start with `XamlParseException` | `Toolbox.pri`, `resources.pri` or `*.xbf` files are missing from the install folder. Republish with `publish.ps1` |
| "No domain controller could be reached" | Network or VPN. With `ldap.server` set, check that server |
| "The directory requires a stronger connection" | The DC rejected the bind settings. Try `ldap.useLdaps: true` with `ldap.server` set to a DC that has a certificate |
| "The configured search root does not exist" | `ldap.rootOu` is wrong, or your account can't see it |
| "Showing the first N computers" | More results than `ldap.maxResults`. Narrow the search |
| Installed OS / last user shows "RPC service is unavailable" or "Timed out" | The PC is offline, a firewall blocks RPC/SMB, or the Remote Registry service is stopped. Needs local admin rights on the target |
| LAPS: "No LAPS password is available" | LAPS doesn't manage that computer, or your account lacks read permission. Legacy LAPS can't tell these apart |
| LAPS: "could not be decrypted" | Encrypted Windows LAPS: your account (or a group it's in) isn't the authorized decryptor for that computer |
| Remote tool missing from the list | None of its `candidatePaths` exists on this PC, or the path breaks a rule (relative, not `.exe`). Configuration problems appear in the status log at start |
| "Update not installed: … untrusted certificate" | The release was signed by a certificate that isn't pinned. Check `trustedSignerThumbprints` / `TrustedUpdateSigners` |
| "Update not installed: … failed the integrity check" | A file on the share doesn't match the manifest, usually `appsettings.json` edited without running `Update-ToolboxManifest.ps1` |
| "No toolbox-manifest.json was found" | The share holds a release published before 3.0. Republish with `publish.ps1` |
| Updates never happen | The folder has no `toolbox-install.json` (run `Install-Toolbox.ps1` once), updates are switched off, or Toolbox runs from the share itself. The log line "Update check skipped" says which |
| Settings not sticking | Personal settings live in `%LocalAppData%\Toolbox\appsettings.json`. Some settings (access control, update trust) can only be set in the install file or by policy |
| `mt.exe` blocked or exits with 1 during build | Allow the Windows SDK `mt.exe` in AppLocker/WDAC, and build x64 |
| XAML compiler fails with `output.json` | Build with Visual Studio's MSBuild (`publish.ps1` does this), not a bare `dotnet publish` |
