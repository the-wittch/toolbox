#Requires -Version 5.1
<#
.SYNOPSIS
Installs Toolbox for the current user from a published release folder. No admin rights needed.

.DESCRIPTION
Every file is checked against toolbox-manifest.json (and its signature, when a trusted
thumbprint is configured) before it is put in place. The install folder gets a
toolbox-install.json so Toolbox can update itself from the same source later.

.EXAMPLE
\\fileserver\apps\Toolbox\Install-Toolbox.ps1

.EXAMPLE
.\Install-Toolbox.ps1 -SourcePath \\fileserver\apps\Toolbox -TrustedThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
#>
[CmdletBinding()]
param(
    [string]$SourcePath = $PSScriptRoot,
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\Toolbox'),
    [string[]]$TrustedThumbprint,
    [switch]$NoShortcut,
    [switch]$NoLaunch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ToolboxRelease.psm1') -Force

if ([string]::IsNullOrWhiteSpace($SourcePath) -or -not (Test-Path -LiteralPath (Join-Path $SourcePath 'Toolbox.exe'))) {
    throw "Toolbox.exe was not found in '$SourcePath'. Pass -SourcePath with the published folder, for example \\fileserver\apps\Toolbox."
}
$source = (Resolve-Path -LiteralPath $SourcePath).ProviderPath.TrimEnd('\')
$Destination = [System.IO.Path]::GetFullPath($Destination).TrimEnd('\')
if ($source -eq $Destination) { throw 'Source and destination are the same folder.' }

if (-not $PSBoundParameters.ContainsKey('TrustedThumbprint')) {
    $policy = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Toolbox' -Name 'TrustedUpdateSigners' -ErrorAction SilentlyContinue
    $TrustedThumbprint = if ($policy) { @($policy.TrustedUpdateSigners) } else { @() }
}

$running = Get-Process -Name Toolbox -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and ([System.IO.Path]::GetDirectoryName($_.Path) -eq $Destination) }
if ($running) { throw "Toolbox is running from $Destination. Close it and run the installer again." }

Write-Host "Checking release in $source"
$release = Test-ToolboxRelease -Path $source -TrustedThumbprint $TrustedThumbprint
if ($release.Signed) { Write-Host "  Signed by $($release.Signer)" } else { Write-Host '  Not signed (file hashes checked only)' }
Write-Host "Installing Toolbox $($release.Version) to $Destination"

$staging = Join-Path $Destination '.toolbox-update\installer'
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    # copy first and hash the copy, so a file swapped on the share mid-install is caught
    foreach ($file in $release.Manifest.files) {
        $relative = $file.path.Replace('/', '\')
        $target = Join-Path $staging $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath (Join-Path $source $relative) -Destination $target -Force
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256.ToUpperInvariant()) {
            throw "Integrity check failed while copying $($file.path)."
        }
    }

    foreach ($file in $release.Manifest.files) {
        $relative = $file.path.Replace('/', '\')
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Move-Item -LiteralPath (Join-Path $staging $relative) -Destination $target -Force
    }
}
finally {
    Remove-Item -LiteralPath (Join-Path $Destination '.toolbox-update') -Recurse -Force -ErrorAction SilentlyContinue
}

$installInfo = [ordered]@{
    updateSourcePath = $source
    installedVersion = $release.Version
    installedUtc     = [DateTime]::UtcNow.ToString('o')
}
[System.IO.File]::WriteAllText((Join-Path $Destination 'toolbox-install.json'), ($installInfo | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))

$exe = Join-Path $Destination 'Toolbox.exe'
if (-not $NoShortcut) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Toolbox.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $Destination
    $shortcut.Description = 'Toolbox'
    $icon = Join-Path $Destination 'Assets\AppIcon.ico'
    $shortcut.IconLocation = if (Test-Path -LiteralPath $icon) { "$icon,0" } else { "$exe,0" }
    $shortcut.Save()
    Write-Host "Desktop shortcut: $shortcutPath"
}

Write-Host ''
Write-Host "Installed Toolbox $($release.Version)."
Write-Host "  App:            $exe"
Write-Host "  Update source:  $source"
Write-Host ''

if (-not $NoLaunch) {
    Start-Process -FilePath $exe -WorkingDirectory $Destination
}
