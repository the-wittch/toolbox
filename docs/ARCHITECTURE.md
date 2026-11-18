# Architecture

Two projects plus tests:

- **`src/Toolbox.Core`** (`net10.0`) holds the logic that doesn't need Windows: settings layering and validation, LDAP filter and path building, rules for launching programs and opening links, release manifests (parsing, signature check, staging, applying), LAPS attribute parsing, error messages and the file log. Its tests run on Linux and Windows.
- **`src/Toolbox`** (`net10.0-windows10.0.19041.0`, WinUI 3, Windows App SDK 1.8) holds the window, the dialogs, and thin services that talk to Windows: ADSI, the remote registry, ping, the clipboard, process start, DPAPI-NG and the registry policy key.

There's no DI container or MVVM framework. `App` creates the services once at startup and exposes them as static properties, and the window code-behind calls them. For an app this size that's easier to follow than the alternatives.

## Startup

1. `App()` starts the log, loads settings (install file, personal file, machine policy) and applies the theme. A broken install file is remembered and reported in `OnLaunched`, and Toolbox exits.
2. `OnLaunched` creates the services and evaluates access control. On a denial, a minimal window shows the deny message and the app exits.
3. `MainWindow` loads. It reports configuration problems and the result of the last update, offers the local install if running from a share, shows the first-run dialog if needed, and then checks for updates in the background.

## Search

`MainWindow.RunSearchAsync`
→ `AdComputerSearcher.SearchAsync`
→ `ComputerSearchQueryBuilder` (Core) builds `(&<base filter>(|(cn=*term*)(description=*term*)))` with the term escaped per RFC 4515
→ `DirectorySearcher` on a background thread, bound with Kerberos signing and sealing (or LDAPS), with a size limit, server time limit and client timeout.

Cancel (or Esc) returns control right away. The directory call itself can't be interrupted, so it finishes within its timeout in the background.

## Details

Selecting a result shows the AD data at once (OS, last logon). After a short pause, `ComputerInfoService` pings the machine and reads the remote registry at the same time, each with its own timeout. Names that aren't valid host names are never queried or passed to tools.

## LAPS

`LapsService` binds directly to the computer object's DN, so there's no name ambiguity, over the same signed and sealed connection. It reads, in order:

1. `msLAPS-EncryptedPassword`, decrypted with `NCryptUnprotectSecret` as the signed-in user, so the domain controller decides whether that user may decrypt it
2. `msLAPS-Password`
3. `ms-Mcs-AdmPwd`

It also reads the matching expiration attribute. The result goes back to the window, which shows the password for `laps.revealSeconds` or puts it on the clipboard, and then drops it. `LapsResult.ToString()` never includes the password. The log only records that a lookup happened and its outcome.

`SecureClipboard` stores the password with clipboard history and cloud sync turned off. It remembers the clipboard sequence number and a salted hash of the password, not the password itself, and later clears the clipboard only if the password is still what's on it.

## Launching programs

`RemoteToolService` resolves the executable with `ExecutableResolver`:

- bare names come only from the Windows folders
- other paths must be a full path to an `.exe`
- no scripts, shortcuts or relative paths

It splits the argument template into arguments before filling in the computer name, and builds the command line with `WindowsCommandLine.Join`, which quotes each argument the way `CommandLineToArgvW` reads it back. Programs start without the shell. If Windows reports that elevation is required, it retries with the UAC prompt.

Links go through `UrlPolicy` (absolute http/https only) and open in the default browser.

## Updates and local install

The pieces, all in Core:

- `ReleaseManifest`
- `ManifestVerifier`, which checks an RSA/SHA-256 signature against pinned thumbprints
- `UpdateStager`, which copies the changed files into `.toolbox-update\staged` while hashing them
- `UpdateApplier`, which renames each current file to `*.toolbox-old`, renames the staged file into its place, rolls back on error, and keeps a journal so an interrupted apply can be completed

`UpdateService` decides whether a folder may be updated:

- unpackaged
- local, not a share
- contains `toolbox-install.json`
- has a source configured

It stages at startup and applies when the window closes, holding a named mutex. `LocalInstallService` uses the same stager and applier for the first install. `scripts/ToolboxRelease.psm1` writes and checks the same manifest format, and a test fixture produced by that module is verified by the C# code in the test suite.

## Settings

`SettingsStore` (Core) merges the personal file over the install file key by key and writes back only the differences. `SettingsService` (app) adds the policy key and re-validates after every save. `SettingsValidator` produces the errors and warnings shown at startup and in the dialogs. `SettingsLimits` clamps numeric values wherever they're used.
