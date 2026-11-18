# Security

## Reporting a problem

Please report vulnerabilities privately through GitHub's "Report a vulnerability" button on the Security tab, not in a public issue. Expect a reply within a week.

Fixes go into the latest release only.

## Security model

Toolbox has no server component and no credentials of its own. Everything it does runs as the signed-in Windows user:

| Operation | Protected by | What the user needs |
|-----------|--------------|---------------------|
| Computer search | AD read permissions | Read access to the OU (default for domain users) |
| LAPS (legacy, Windows LAPS clear text) | AD confidential attribute ACL | "Read password" permission on the OU (`Set-AdmPwdReadPasswordPermission` / `Set-LapsADReadPasswordPermission`) |
| LAPS (Windows LAPS encrypted) | DPAPI-NG, checked by the DC | Membership of the configured authorized decryptor group |
| Installed OS, last logged-on user | Remote registry ACL | Local admin on the target, Remote Registry service running |
| Remote tools | The tools themselves | Whatever each tool requires |

**The `accessControl` group check is not a security boundary.** It keeps Toolbox out of the hands of people who shouldn't use it day to day. Anyone who could read a LAPS password with their own account can do so with PowerShell too. Control access with AD permissions. Set the group check by Group Policy (`AllowedGroups`) if you want users unable to switch it off: in a per-user install the install folder, and with it `appsettings.json`, is writable by that user. Toolbox refuses to start if access control is enabled with no groups, or if the policy key can't be read.

Auditing of LAPS access belongs on the domain controllers: enable Directory Service Access auditing and watch event 4662 on the LAPS attributes (Windows LAPS also logs its own events). Toolbox writes a line to its local log for each lookup. That log is under the user's control and is no audit trail.

## What Toolbox does to protect data

**LDAP**
- Connections are Kerberos-signed and encrypted, or use LDAPS with normal certificate validation (which can't be turned off).
- Search text is escaped per RFC 4515.
- The administrator's base filter and attribute names are validated.
- Every search has a size limit and timeouts.

**LAPS**
- A password is read only when the user clicks Show or Copy.
- It is hidden again after 20 seconds and dropped when another computer is selected.
- It is never logged, and `LapsResult.ToString()` redacts it.

**Clipboard**
- A copied password is kept out of Windows clipboard history and cloud clipboard.
- After 30 seconds, and again when Toolbox exits, it is cleared, but only if it is still on the clipboard. Text copied afterwards is left alone.

**Starting programs**
- Paths: only `.exe` files at full paths, or bare names from the Windows folders. No scripts, shortcuts, relative paths or paths built from the computer name.
- Arguments: each is quoted separately, so a value can't add arguments.
- Computer names: must be valid host names, because anyone who can create computer objects chooses them.
- Programs start without the shell.

**Links**
- Only absolute http(s) URLs without embedded credentials, opened in the browser.

**Updates**
- Every file is checked against a manifest (size and SHA-256).
- The manifest can be signed and the signer pinned by thumbprint.
- Lower versions are refused, and only folders Toolbox installed are updated.
- Files are swapped by rename with rollback.
- No scripts or command interpreters are involved.

**Settings**
- Toolbox never writes the install configuration.
- Access-control and update-trust settings can't be overridden per user.
- A broken install configuration stops Toolbox instead of falling back to defaults.

## Known limitations

- **.NET strings can't be wiped.** A retrieved password lives in managed memory until the garbage collector reuses it. Toolbox keeps as few copies as it can, for as short a time as it can. It does not claim to erase them.
- **Other clipboard tools.** Third-party clipboard managers may ignore the "no history" flag and keep a copy.
- **First install.** Without a pinned certificate, the first install trusts whatever is on the share. With a pin in the share's `appsettings.json` but not in Group Policy, the same applies to the first install only.
- **Unsigned releases** prove integrity (nothing was corrupted or partly copied), not authenticity.
- **No certificate chain or revocation checks on releases.** The pinned thumbprint is the trust anchor. If the signing key is compromised, remove its thumbprint.
- **LDAP channel binding.** LDAPS relies on the channel binding support built into Windows. Plain LDAP depends on the domain's signing policy, which Toolbox meets.
- **Legacy LAPS can't distinguish** "not managed" from "no permission".
- **Remote Registry** requires admin rights on the target and an RPC path to it.

## Supply chain

- Release builds run on GitHub-hosted runners from tags on `main` and record build provenance.
- `SHA256SUMS.txt` is attached to each release.
- Actions are pinned to commit SHAs and kept current by Dependabot, and NuGet packages are watched by Dependabot too.
- CodeQL scans the C# code and the workflows.
