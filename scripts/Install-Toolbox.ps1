#Requires -Version 5.1
<#
.SYNOPSIS
  Installs Toolbox for the current Windows user (no admin required).

.DESCRIPTION
  Copies the published Toolbox folder into:
    %LOCALAPPDATA%\Programs\Toolbox
  Creates a Desktop shortcut, and optionally launches the app.

  Give staff this script (or a shortcut to it) from the file share. Example:
    \\7jd-mon-fs1\apps\Toolbox\Install-Toolbox.ps1

.EXAMPLE
  .\Install-Toolbox.ps1
  .\Install-Toolbox.ps1 -SourcePath '\\7jd-mon-fs1\apps\Toolbox'
  .\Install-Toolbox.ps1 -NoLaunch
#>
param(
    [string]$SourcePath = '',
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\Toolbox'),
    [switch]$NoShortcut,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

function Resolve-SourcePath {
    param([string]$Requested)

    if (-not [string]::IsNullOrWhiteSpace($Requested) -and (Test-Path -LiteralPath $Requested)) {
        return (Resolve-Path -LiteralPath $Requested).Path
    }

    # When the script lives inside the published share folder, use that folder.
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    if (Test-Path -LiteralPath (Join-Path $scriptDir 'Toolbox.exe')) {
        return $scriptDir
    }

    $defaultShare = '\\7jd-mon-fs1\apps\Toolbox'
    if (Test-Path -LiteralPath (Join-Path $defaultShare 'Toolbox.exe')) {
        return $defaultShare
    }

    throw "Could not find Toolbox.exe. Pass -SourcePath to the published folder (for example \\7jd-mon-fs1\apps\Toolbox)."
}

$source = Resolve-SourcePath -Requested $SourcePath
$exeName = 'Toolbox.exe'
$sourceExe = Join-Path $source $exeName
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "Toolbox.exe not found in '$source'."
}

Write-Host "Installing Toolbox"
Write-Host "  From: $source"
Write-Host "  To:   $Destination"

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$robocopyArgs = @(
    $source,
    $Destination,
    '/E', '/XO', '/R:2', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS',
    '/XD', '.git'
)
& robocopy @robocopyArgs | Out-Null
$rc = $LASTEXITCODE
if ($rc -ge 8) {
    throw "robocopy failed with exit code $rc"
}

$destExe = Join-Path $Destination $exeName
if (-not (Test-Path -LiteralPath $destExe)) {
    throw "Install finished but '$destExe' is missing."
}

# Point auto-update back at the share/source folder.
$appsettingsPath = Join-Path $Destination 'appsettings.json'
try {
    if (Test-Path -LiteralPath $appsettingsPath) {
        $json = Get-Content -LiteralPath $appsettingsPath -Raw | ConvertFrom-Json
    }
    else {
        $json = [pscustomobject]@{}
    }

    if (-not $json.deployment) {
        $json | Add-Member -NotePropertyName deployment -NotePropertyValue ([pscustomobject]@{}) -Force
    }
    $json.deployment | Add-Member -NotePropertyName updateSourcePath -NotePropertyValue $source -Force
    $json.deployment | Add-Member -NotePropertyName autoUpdateOnClose -NotePropertyValue $true -Force
    $json | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $appsettingsPath -Encoding UTF8
}
catch {
    Write-Warning "Installed files, but could not patch appsettings.json deployment path: $($_.Exception.Message)"
}

$shortcutPath = $null
if (-not $NoShortcut) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shortcutPath = Join-Path $desktop 'Toolbox.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $destExe
    $shortcut.WorkingDirectory = $Destination
    $shortcut.Description = 'Toolbox'
    $iconPath = Join-Path $Destination 'Assets\AppIcon.ico'
    if (Test-Path -LiteralPath $iconPath) {
        $shortcut.IconLocation = "$iconPath,0"
    }
    else {
        $shortcut.IconLocation = "$destExe,0"
    }
    $shortcut.Save()
    Write-Host "Desktop shortcut: $shortcutPath"
}

Write-Host ""
Write-Host "Installed successfully."
Write-Host "  App: $destExe"
Write-Host "  On close, newer files can sync from: $source"
Write-Host ""

if (-not $NoLaunch) {
    Start-Process -FileName $destExe -WorkingDirectory $Destination
}
