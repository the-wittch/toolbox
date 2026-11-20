# Configuration

Toolbox reads its settings from three places. Later layers win.

1. **Install settings**: `appsettings.json` next to `Toolbox.exe`. On a share or in an MSIX package this is the file administrators maintain. Toolbox never writes to it.
2. **Personal settings**: `%LocalAppData%\Toolbox\appsettings.json`. Written by the first-run dialog, the Settings dialog and the theme picker. It holds only the values a user changed, so later edits to the install settings still reach everyone.
3. **Machine policy**: `HKLM\SOFTWARE\Policies\Toolbox`. Standard users cannot change it, which makes it the right place for access control and update trust.

Some settings are taken from the install settings (or policy) only, whatever the personal file says: `accessControl`, `deployment.trustedSignerThumbprints`, `deployment.requireSignedUpdates` and `deployment.allowDowngrade`.

In the personal file, an empty string, empty list or `null` means "use the install value".

Keys are case-insensitive. Comments and trailing commas are allowed.

## What happens when a file is broken

- Install settings that are not valid JSON stop Toolbox with a message naming the file and line. Falling back to defaults could silently switch off access control.
- A broken personal file is renamed to `appsettings.json.corrupt-<timestamp>`, and Toolbox carries on with the install settings.
- Settings with invalid values are listed in the status bar and in the log at startup. Numeric values outside their allowed range are clamped.

## Settings reference

### Top level

| Key | Default | |
|-----|---------|---|
| `programTitle` | `Toolbox` | Window title |
| `theme` | `System` | `System`, `Light` or `Dark` |
| `setupCompleted` | `false` | Set to `true` in the install settings to skip the first-run dialog for everyone |

### `ldap`

| Key | Default | |
|-----|---------|---|
| `rootOu` | placeholder | Search root as a distinguished name, for example `OU=Computers,DC=contoso,DC=com`. A full `LDAP://server/DN` path is also accepted and used as written |
| `server` | empty | Domain controller or domain name. Empty lets Windows pick a DC |
| `useLdaps` | `false` | Connect over LDAPS (port 636). Without it the connection is signed and encrypted with Kerberos |
| `pageSize` | `1000` | 1–1000 |
| `maxResults` | `500` | Results shown per search, 1–5000. Toolbox tells you when there are more |
| `searchTimeoutSeconds` | `30` | 5–300 |
| `computerObjectFilter` | `(objectCategory=computer)` | Base filter, must be a single well-formed LDAP filter |
| `nameAttribute` | `cn` | Attribute holding the computer name |
| `descriptionAttribute` | `description` | Attribute searched and shown as description |
| `missingDescriptionLabel` | `No description available` | |

A search term that contains one of `search.computerNamePrefixes` matches computer names only. Any other term matches the name or the description. The term is escaped before it goes into the LDAP filter, so `*`, `(` and `)` are matched literally.

`propertiesToLoad` from 2.x is no longer used and can be removed.

### `search`

| Key | Default | |
|-----|---------|---|
| `computerNamePrefixes` | `["PC-", "WS-"]` | See above |
| `presets` | | `{ "id", "label", "queryText" }`. `{today:M/d/yyyy}` in `queryText` becomes today's date |
| `historyLimit` | `50` | 1–500 |

### `accessControl` (install settings or policy only)

| Key | Default | |
|-----|---------|---|
| `enabled` | `false` | |
| `allowedGroups` | | Group names (`CONTOSO\Help Desk`) or SIDs (`S-1-5-21-…`). SIDs keep working if a group is renamed |
| `denyMessage` | | Shown to users who are not allowed in |

When enabled with no groups, nobody gets in. Membership comes from the user's logon token, so a group change takes effect at the user's next sign-in.

This check decides who may open Toolbox. It does not protect anything on its own: what a user can read is decided by Active Directory permissions. See [SECURITY.md](../SECURITY.md).

### `ping`, `details`

| Key | Default | |
|-----|---------|---|
| `ping.timeoutMs` | `3000` | 100–60000 |
| `ping.bufferSize` | `32` | |
| `details.remoteRegistryTimeoutSeconds` | `15` | Limit for reading OS build and last user from the remote registry |

### `laps`

| Key | Default | |
|-----|---------|---|
| `enabled` | `true` | Shows the Show and Copy buttons |
| `clipboardClearSeconds` | `30` | A copied password is removed from the clipboard after this time, if it is still there. `0` turns clearing off |
| `revealSeconds` | `20` | A shown password is hidden again after this time |
| `lookupTimeoutSeconds` | `20` | |

### `deployment`

| Key | Default | |
|-----|---------|---|
| `updateSourcePath` | empty | Folder holding the published release, for example `\\fileserver\apps\Toolbox`. Installs created by `Install-Toolbox.ps1` remember their source in `toolbox-install.json`, so this can stay empty |
| `autoUpdateOnClose` | `true` | Check the source at startup and install a newer verified release on close. The shipped file sets it to `false` |
| `trustedSignerThumbprints` | `[]` | Admin only. SHA-1 or SHA-256 thumbprints of the certificate that signs releases. When set, unsigned or differently signed releases are refused |
| `requireSignedUpdates` | `false` | Admin only. Refuse unsigned releases. Needs at least one thumbprint |
| `allowDowngrade` | `false` | Admin only. Allow a lower version on the share to replace the installed one |

### `remoteTools`

```json
{
  "id": "rdp",
  "label": "RDP",
  "enabled": true,
  "arguments": "/v:{computerName}",
  "candidatePaths": [ "mstsc.exe" ],
  "preferAsDefault": false
}
```

- `candidatePaths` are tried in order; the first one that exists is used.
- A bare file name such as `mstsc.exe` is looked up in the Windows and System32 folders only.
- Anything else must be a full path to an `.exe` (`C:\…` or `\\server\share\…`). `%VARIABLES%` and `{userName}` are expanded.
- Relative paths, scripts (`.bat`, `.cmd`, `.ps1`), shortcuts and paths containing `{computerName}` are rejected.
- `arguments` is split into separate arguments before `{computerName}` and `{userName}` are filled in, so a value can never add extra arguments. Use double quotes to group text with spaces.
- Computer names are only passed on if they are plain host names (letters, digits, `-`, `_`, dots).

If a tool needs elevation, Windows shows the UAC prompt.

### `detailActions`

Buttons in the details pane, for example "Open C$". Same rules as remote tools, with `executable` instead of `candidatePaths`. `requiresOnline` disables the button until the computer answers ping.

### `streaming`

| Key | |
|-----|---|
| `enabled` | Show the Streaming tab when at least one endpoint is configured |
| `connectRemoteToolId` | Remote tool used by "Connect to PC" |
| `changePasswordUrl` | Link for the password button |
| `endpoints[]` | `id`, `label`, `computerName`, `viewStreamUrl`, `controlAppUrl` |

Links must be absolute `http` or `https` URLs without a user name or password. They open in the default browser; Toolbox does not fetch them itself. Plain `http` links work, but the status bar notes that they are unencrypted.

### `labels`, `ui`

Text shown in the main window, window size, and whether the history column and status log are visible.

## Machine policy

`HKLM\SOFTWARE\Policies\Toolbox`. All values are optional.

| Value | Type | Effect |
|-------|------|--------|
| `AllowedGroups` | REG_MULTI_SZ | Turns on access control with these groups |
| `DenyMessage` | REG_SZ | Message for users who are refused |
| `UpdateSourcePath` | REG_SZ | Overrides the update source |
| `AutoUpdate` | REG_DWORD | `0` turns updates off, `1` turns them on |
| `TrustedUpdateSigners` | REG_MULTI_SZ | Signing certificate thumbprints. Setting it makes signed releases mandatory |

If the key exists but cannot be read, Toolbox refuses to start.

## Files Toolbox writes

All under `%LocalAppData%\Toolbox`:

| File | |
|------|---|
| `appsettings.json` | Personal settings |
| `search-history.json` | Recent searches |
| `logs\toolbox-yyyyMMdd.log` | Daily log, kept 14 days, at most 10 MB a day |
| `logs\crash-*.log` | Details of fatal errors, last 10 kept |
| `last-update.json` | Result of the last update, shown at the next start |

Neither the log nor any other file ever contains a LAPS password.
