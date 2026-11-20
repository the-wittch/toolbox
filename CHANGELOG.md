# Changelog

## 3.0.0 (unreleased)

### Breaking changes

- **.NET 10.** .NET 8 reached end of support on 2026-11-10. Building now needs the .NET 10 SDK; release builds stay self-contained.
- **Unpackaged updates need a release manifest.** `publish.ps1` writes `toolbox-manifest.json`. After editing files on the share, run `Update-ToolboxManifest.ps1`, or installed copies will refuse the release. 2.x installs pick up 3.0 through their old updater and switch to the new mechanism.
- **Remote tools and detail actions** must point at an `.exe`, either with a full path or as a bare name found in the Windows folders. Relative paths, scripts and `{computerName}` in executable paths are rejected.
- **Streaming links** must be absolute http or https URLs.
- **Access control and update trust** are read from the install settings or Group Policy only. The Settings dialog no longer edits access control.
- **Personal settings files** now store only changed values. Settings saved by 2.x hold a full copy; use *Reset to install defaults* once so later changes on the share reach those users.
- **`ldap.propertiesToLoad`** is ignored.

### Security

- LAPS passwords are read only on Show or Copy, hidden again automatically, kept out of clipboard history and cloud clipboard, and cleared from the clipboard after 30 seconds.
- Updates are verified file by file against the manifest, can be signed and pinned, refuse downgrades, and only touch folders Toolbox installed. The batch-file updater and its path interpolation are gone.
- LDAP connections request Kerberos signing and sealing; LDAPS is available.
- Search runs as an escaped server-side LDAP filter.
- Computer names from AD are validated before reaching ping, the remote registry or a command line, and arguments are quoted individually.
- Bare program names resolve only from the Windows folders, so a file planted next to Toolbox can't be started instead.
- Access control fails closed: with no groups, on lookup errors, or when the policy key can't be read. Groups can be given as SIDs.
- Toolbox no longer writes the install `appsettings.json`. Before, changing the theme rewrote the share's file whenever it was writable.
- A broken install configuration stops Toolbox instead of silently using defaults. A broken personal file is set aside.

### Added

- Windows LAPS support (`msLAPS-EncryptedPassword`, `msLAPS-Password`), with account name and expiry.
- Machine policy under `HKLM\SOFTWARE\Policies\Toolbox`.
- Cancel button, result count and truncation notice for searches; OS column in results.
- Details load on selection. AD OS and last directory logon show immediately; the installed OS shows Windows 11 correctly.
- Ping and remote-registry lookups run in parallel with timeouts.
- Keyboard: Ctrl+F (search), Esc (cancel), F5 (refresh details), Enter (open the selected result or history entry).
- Screen reader names and a live status line; theme-aware ping indicator; accent buttons use the system accent style.
- Clear history, Open log folder, update notifications.
- Daily log file with retention; timestamped crash logs.
- In-app local install runs in the background with progress and verification.
- Install-Toolbox.ps1 verifies before installing and writes `toolbox-install.json`.
- `publish-msix.ps1` signs from the certificate store, checks the publisher, verifies the signature and writes a checksum.
- `Toolbox.Core` library with 220+ unit tests; Pester tests for the release module.
- CI on pull requests, CodeQL, NuGet Dependabot, release checksums and build provenance.

### Fixed

- Search no longer downloads every computer in the OU and filters on the client.
- Settings missing from a personal file no longer reset install values to built-in defaults.
- Saving settings can no longer leave the app with half-applied changes.
- Process handles are released after starting tools.
- `publish.ps1` stops on errors, handles paths with spaces, and refuses to wipe a non-empty output folder.
- The `mt.exe` path follows the BuildTools package version.

## 2.0.0

- WinUI 3 rewrite with first-run setup, per-user settings, local install, Streaming tab, and MSIX packaging.
