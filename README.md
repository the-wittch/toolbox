# Toolbox

[![CI](https://github.com/the-wittch/toolbox/actions/workflows/ci.yml/badge.svg)](https://github.com/the-wittch/toolbox/actions/workflows/ci.yml)
[![Release](https://github.com/the-wittch/toolbox/actions/workflows/release.yml/badge.svg)](https://github.com/the-wittch/toolbox/actions/workflows/release.yml)

Toolbox is a WinUI 3 desktop app for help-desk staff. It finds computers in Active Directory, shows their OS, last logged-on user and ping state, reveals or copies the LAPS password, and starts remote tools such as Remote Assistance, RDP or the Configuration Manager remote viewer.

It runs under the signed-in user's own account. Toolbox never asks for or stores credentials; what you can see is what Active Directory lets your account see.

## Requirements

- Windows 10 1809 (build 17763) or later, or Windows 11, x64
- To build: a Visual Studio release that supports .NET 10, with the .NET desktop and Windows application development workloads, and the .NET 10 SDK
- Windows App SDK 1.8 comes from NuGet; release builds are self-contained, so clients need nothing installed

## Features

- Server-side LDAP search by computer name or description, with presets and history
- AD details straight from the search result: OS, last directory logon
- Live details from the computer: installed OS build, last logged-on user, ping
- LAPS: Windows LAPS (encrypted and clear text) and legacy Microsoft LAPS. The password is fetched only when you click Show or Copy, hidden again after a few seconds, kept out of clipboard history and cleared from the clipboard after 30 seconds
- Remote tools and quick actions defined in `appsettings.json`
- Optional Streaming tab
- Optional AD group restriction, also deployable by Group Policy
- Unpackaged install per user with verified, optionally signed updates; or MSIX for Configuration Manager / Intune

## Quick start

```powershell
git clone https://github.com/the-wittch/toolbox.git
cd toolbox
dotnet test tests\Toolbox.Core.Tests          # runs on any OS
msbuild Toolbox.sln /restore /p:Configuration=Debug /p:Platform=x64
```

Or open `Toolbox.sln` in Visual Studio, pick x64 and press F5.

## Deploying

Short version for a file share:

```powershell
.\scripts\publish.ps1 -CertificateThumbprint <code-signing cert thumbprint>
# copy dist\Toolbox to \\fileserver\apps\Toolbox
```

Staff run `\\fileserver\apps\Toolbox\Install-Toolbox.ps1` once. After that, Toolbox checks the share at startup and installs newer releases when it closes, but only after every file matches the signed manifest.

For MSIX, run `.\scripts\publish-msix.ps1 -CertificateThumbprint <thumbprint>` and deploy the package with your management tool.

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for the full picture, including update signing and Group Policy.

## Documentation

| Document | Covers |
|----------|--------|
| [docs/CONFIGURATION.md](docs/CONFIGURATION.md) | Every setting, settings layers, machine policy |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Share install, updates and signing, MSIX, release workflow |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | How the code is organised and how the main operations work |
| [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) | Logs, common errors and fixes |
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | Building, testing, CI |
| [SECURITY.md](SECURITY.md) | Security model, permissions, known limitations, reporting |
| [CHANGELOG.md](CHANGELOG.md) | Release notes |

## Project layout

```
Toolbox.sln
global.json                        .NET SDK selection
Directory.Build.props / .targets   force x64; unpackaged PRI publish fix
src/Toolbox/                       WinUI app: windows, dialogs, Windows-specific services
src/Toolbox.Core/                  platform-neutral logic: settings, LDAP filters, launch rules,
                                   update manifests, LAPS parsing, logging
tests/Toolbox.Core.Tests/          xUnit tests for Toolbox.Core
tests/scripts/                     Pester tests for the release module
scripts/                           publish, sign, install
docs/                              documentation
```

## License

MIT, see [LICENSE](LICENSE).
