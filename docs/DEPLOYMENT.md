# Deployment

There are two ways to roll Toolbox out. Pick one per machine. Don't mix them.

| | Unpackaged (file share) | MSIX |
|---|---|---|
| Install | `Install-Toolbox.ps1` per user, no admin | Configuration Manager, Intune or `Add-AppxPackage` |
| Location | `%LocalAppData%\Programs\Toolbox` | `C:\Program Files\WindowsApps` |
| Updates | Toolbox updates itself from the share | Your deployment tool |
| Install settings | `appsettings.json` on the share | `appsettings.json` inside the package (rebuild to change) |

Self-update and the "install on this PC" prompt are switched off in MSIX installs.

## Unpackaged deployment from a share

### 1. Publish

```powershell
.\scripts\publish.ps1 -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
```

This builds a self-contained release into `dist\Toolbox`, then writes `toolbox-manifest.json` with the size and SHA-256 hash of every file. With a certificate it also writes `toolbox-manifest.sig` and Authenticode-signs the PowerShell scripts.

- `-CertificateThumbprint` uses a certificate from `CurrentUser\My` or `LocalMachine\My`. Smart cards and HSM-backed keys work.
- `-CertificatePath file.pfx -CertificatePassword (Read-Host -AsSecureString)` uses a PFX instead.
- Without a certificate the release is unsigned. Clients still check every file hash, but they can't tell who published the release.

The script fails if `Toolbox.pri` or `resources.pri` is missing from the output. Unpackaged WinUI apps crash at startup with `XamlParseException` without them (microsoft/WindowsAppSDK#6720), and `Directory.Build.targets` exists to put them there.

`-OutputDir` must be empty, or pass `-Clean`. Publish to `dist` and copy, rather than straight onto the live share.

### 2. Put it on the share

Copy the whole `dist\Toolbox` folder to the share, for example `\\fileserver\apps\Toolbox`, then edit `appsettings.json` there for your site.

**After editing any file on the share, rebuild the manifest:**

```powershell
\\fileserver\apps\Toolbox\Update-ToolboxManifest.ps1 -CertificateThumbprint <thumbprint>
```

Installed copies refuse a release whose files don't match its manifest. That includes `appsettings.json`, because it decides which programs Toolbox starts.

Share permissions: staff need read access only. Anyone who can write to the share can change what help-desk staff run. Signing limits the damage, but write access should still be admin-only.

### 3. Pin the signing certificate

Tell clients which certificate to trust, by either method:

- in `appsettings.json` on the share: `"deployment": { "trustedSignerThumbprints": [ "<thumbprint>" ] }`
- or by Group Policy: `HKLM\SOFTWARE\Policies\Toolbox\TrustedUpdateSigners` (REG_MULTI_SZ)

Group Policy is the stronger choice. The settings file on the share is itself covered by the signature, so once a client trusts a certificate, a release that removes the pin must still be signed by that certificate. But the very first install trusts whatever is on the share at that moment.

`publish.ps1` prints the thumbprint to pin. Both SHA-1 and SHA-256 thumbprints are accepted.

When the certificate is renewed, add the new thumbprint next to the old one, publish with the new certificate, then remove the old thumbprint.

### 4. Install for each user

Staff run, without admin rights:

```powershell
\\fileserver\apps\Toolbox\Install-Toolbox.ps1
```

The installer checks the manifest and signature, copies every file into a staging folder, hashes the copies, and only then moves them into `%LocalAppData%\Programs\Toolbox`. It writes `toolbox-install.json` there, recording the share as the update source, creates a desktop shortcut and starts Toolbox.

- Trusted thumbprints come from `-TrustedThumbprint`, or else from the `TrustedUpdateSigners` policy.
- `-Destination`, `-NoShortcut` and `-NoLaunch` are available.
- If you require signed scripts (`AllSigned`), sign with a code-signing certificate when publishing.

Starting `Toolbox.exe` straight from the share also offers to install a local copy. That in-app install runs the same checks.

### 5. Updates

On each start, an installed copy looks at its update source:

1. It reads `toolbox-manifest.json` and checks the signature against the pinned thumbprints.
2. If the version is newer than the running one, it copies only the files that differ into `.toolbox-update` inside the install folder, hashing each one.
3. The status bar says an update is ready. When Toolbox closes, the staged files are renamed into place. If anything fails, the files already swapped are put back.
4. The next start reports the result.

Other things to know:

- An older version on the share is ignored unless `deployment.allowDowngrade` is set.
- Updates only happen in folders that contain `toolbox-install.json`. Toolbox never updates a share or any folder it didn't install.
- If another Toolbox window is still open from the same folder, the update waits.
- If Toolbox is killed in the middle of swapping files, the next close finishes the job.

Old installs from 2.x have no `toolbox-install.json`. If they are in the default folder and have an update source configured, Toolbox creates the file on first start. Otherwise run `Install-Toolbox.ps1` once.

## MSIX

1. Set `Publisher` in `src\Toolbox\Package.appxmanifest` to the exact subject of your signing certificate, for example `CN=Contoso IT, O=Contoso, C=US`.
2. Build and sign:

   ```powershell
   .\scripts\publish-msix.ps1 -CertificateThumbprint <thumbprint>
   ```

   The script checks the publisher matches, signs with `signtool /sha1`, runs `signtool verify /pa`, and writes a `.sha256` file next to the package.
3. If the certificate isn't from a CA your clients trust, deploy it to Trusted People or Trusted Root by Group Policy.
4. Create a "Windows app package" application in Configuration Manager (or a line-of-business app in Intune).

Every upgrade needs a higher `Identity Version`. The release workflow sets it from the tag.

## GitHub releases

Pushing a tag such as `v3.0.1` on a commit that is on `main` runs `.github/workflows/release.yml`, which:

- sets the versions in `Toolbox.csproj` and `Package.appxmanifest` from the tag
- runs the tests
- publishes the unpackaged zip and the MSIX
- writes `SHA256SUMS.txt`
- records build provenance (`gh attestation verify <file> --repo the-wittch/toolbox`)
- creates the GitHub release

If the repository secrets `SIGNING_PFX_BASE64` and `SIGNING_PFX_PASSWORD` exist, the manifest and MSIX are signed. Otherwise both are unsigned, and you can sign later with `Update-ToolboxManifest.ps1` and `publish-msix.ps1`.

Running the workflow by hand builds artifacts without creating a release.
