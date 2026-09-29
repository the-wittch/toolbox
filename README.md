# Toolbox

[![Release](https://github.com/the-wittch/toolbox/actions/workflows/release.yml/badge.svg)](https://github.com/the-wittch/toolbox/actions/workflows/release.yml)
[![Dependabot Updates](https://github.com/the-wittch/toolbox/actions/workflows/dependabot/dependabot-updates/badge.svg)](https://github.com/the-wittch/toolbox/actions/workflows/dependabot/dependabot-updates)

WinUI 3 help-desk utility for Active Directory computer search, ping/OS detail, LAPS copy, and launching remote tools (SCCM Remote Viewer, etc.).

Built for internal IT use: unpackaged share install **or** signed MSIX via SCCM.

## Requirements

- Windows 10/11 x64
- Visual Studio 2022 with **.NET desktop** + **Windows application development** workloads
- .NET 8 SDK
- Windows App SDK 1.8 (restored via NuGet)

## Features

- LDAP computer search with presets and history
- Detail pane: OS, last user, ping, LAPS password copy
- Configurable remote tools from `appsettings.json`
- Optional Streaming tab (endpoints configured in settings)
- Optional AD group access control
- First-run setup for OU / prefixes / update path
- Per-user settings overlay under `%LocalAppData%\Toolbox`
- Unpackaged: local install to `%LocalAppData%\Programs\Toolbox` + optional auto-update on close
- Packaged: MSIX for SCCM (self-update / local-install prompts disabled)

## Quick start (dev)

```powershell
git clone https://github.com/the-wittch/toolbox.git
cd toolbox
# Open Toolbox.sln in Visual Studio, set platform to x64, F5
```

Or:

```powershell
dotnet build .\src\Toolbox\Toolbox.csproj -c Debug -p:Platform=x64
```

## Configuration

Defaults ship in `src/Toolbox/appsettings.json` (copied next to `Toolbox.exe` on publish).
Public defaults use placeholders only — put org-specific LDAP roots, UNC shares, and streaming URLs on your **install/share copy** or in Settings (saved under `%LocalAppData%\Toolbox`).

| Area | Purpose |
|------|---------|
| `ldap.rootOu` | Search root (pre-fills first-run) |
| `search.computerNamePrefixes` | Suggested prefixes |
| `search.presets` | Quick filters |
| `accessControl` | Optional AD group gate |
| `deployment.updateSourcePath` | Share path for unpackaged auto-update |
| `streaming` | Optional Streaming tab endpoints |
| `remoteTools` | Paths/args for remote consoles |

**Write rules**

- Prefer editing `appsettings.json` beside the exe (share or install folder).
- If that folder is read-only, saves go to `%LocalAppData%\Toolbox\appsettings.json` and overlay the defaults.
- Set `setupCompleted` to `true` on the share copy only if you want to skip the first-run wizard for everyone.

## Deploy: unpackaged (file share)

Good for xcopy / break-glass. Staff install without admin.

```powershell
.\scripts\publish.ps1
# or straight to the share:
.\scripts\publish.ps1 -OutputDir '\\fileserver\apps\Toolbox'
```

Publish **must** include both `Toolbox.pri` and `resources.pri` (same WinUI resource index, two names). `publish.ps1` fails if either is missing — without them the app crashes at startup with `XamlParseException`.

Staff install:

```powershell
\\fileserver\apps\Toolbox\Install-Toolbox.ps1
```

That copies into `%LocalAppData%\Programs\Toolbox`, creates a Desktop shortcut, and can set auto-update from the share on close.

## Deploy: MSIX + SCCM (recommended for staff)

1. Edit `src/Toolbox/Package.appxmanifest`
   - `Publisher` must **exactly** match your code-signing cert Subject (e.g. `CN=Wittch`)
   - Bump `Version` for every upgrade (`2.0.0.0` → `2.0.1.0`)
2. Build (and optionally sign):

```powershell
.\scripts\publish-msix.ps1 -CertificatePath C:\certs\YourCodeSign.pfx
```

3. SCCM → Create Application → **Windows app package (*.msix)** → deploy to your help-desk collection.
4. Trust the signing cert on clients (GPO / cert profile) if it is not publicly trusted.

Packaged installs are updated only through SCCM. Do not use `Install-Toolbox.ps1` or robocopy auto-update for those machines.

## GitHub Releases (automated)

Push a version tag to build release assets on `windows-latest`. The workflow sets `Toolbox.csproj` and `Package.appxmanifest` versions from the tag (`v2.0.1` → `2.0.1` / `2.0.1.0`):

```powershell
git tag v2.0.1
git push origin v2.0.1
```

The [Release](.github/workflows/release.yml) workflow runs `scripts/publish.ps1` and `scripts/publish-msix.ps1`, then attaches:

- `Toolbox-<tag>-win-x64.zip` — unpackaged self-contained folder (unzip / robocopy to your share)
- `*.msix` — unsigned package (sign locally before SCCM)

You can also run the workflow manually from the Actions tab (`workflow_dispatch`) to produce artifacts without creating a release (keeps versions already in the repo files).

## Project layout

```
Toolbox.sln
Directory.Build.props / .targets   # force x64; unpackaged PRI publish workaround
LICENSE                            # MIT
.github/workflows/release.yml      # tag → zip + MSIX GitHub Release
scripts/
  publish.ps1                      # unpackaged self-contained folder
  publish-msix.ps1                  # MSIX for SCCM
  Install-Toolbox.ps1               # per-user unpackaged installer
src/Toolbox/
  App.xaml(.cs)                    # startup, resources, crash logging
  MainWindow.xaml(.cs)             # main UI
  Package.appxmanifest             # MSIX identity
  appsettings.json                 # public defaults (placeholders)
  Services/                        # LDAP, ping, settings, update, install
  Views/                           # first-run, settings, local-install dialogs
  Assets/                          # icons
```

## Troubleshooting

| Symptom | What to check |
|---------|----------------|
| Silent exit / crash on start | `%LocalAppData%\Toolbox\crash.log` |
| `XamlParseException` at `MainWindow` | Install folder has `Toolbox.pri`, `resources.pri`, and `*.xbf` (republish with `publish.ps1`) |
| `mt.exe` blocked / exit 1 | Unblock Windows SDK `mt.exe` in AppLocker; build **x64** |
| XAML compiler `output.json` | Prefer VS MSBuild via `publish.ps1`, not a clean `dotnet publish` alone |
| Settings not sticking on share | Expected — check `%LocalAppData%\Toolbox\appsettings.json` |

## License

This project is licensed under the [MIT License](LICENSE).

See the [LICENSE](LICENSE) file in the repository root for the full text.
