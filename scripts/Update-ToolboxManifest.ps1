#Requires -Version 5.1
<#
.SYNOPSIS
Rebuilds (and optionally re-signs) the release manifest of a published Toolbox folder.

.DESCRIPTION
Run this after changing files in a published folder, usually appsettings.json on the share.
Installed copies refuse a release whose files no longer match its manifest.

.EXAMPLE
.\Update-ToolboxManifest.ps1 -Path \\fileserver\apps\Toolbox -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
#>
[CmdletBinding(DefaultParameterSetName = 'Unsigned')]
param(
    [string]$Path = $PSScriptRoot,
    [Parameter(ParameterSetName = 'Store')][string]$CertificateThumbprint,
    [Parameter(ParameterSetName = 'Pfx')][string]$CertificatePath,
    [Parameter(ParameterSetName = 'Pfx')][SecureString]$CertificatePassword
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ToolboxRelease.psm1') -Force

$current = Get-Content -LiteralPath (Join-Path $Path 'toolbox-manifest.json') -Raw | ConvertFrom-Json
$wasSigned = Test-Path -LiteralPath (Join-Path $Path 'toolbox-manifest.sig')

$certificate = $null
if ($PSCmdlet.ParameterSetName -eq 'Store') {
    $certificate = Get-ToolboxSigningCertificate -Thumbprint $CertificateThumbprint
}
elseif ($PSCmdlet.ParameterSetName -eq 'Pfx') {
    $certificate = Get-ToolboxSigningCertificate -PfxPath $CertificatePath -PfxPassword $CertificatePassword
}
elseif ($wasSigned) {
    throw 'This release was signed. Pass -CertificateThumbprint or -CertificatePath so it stays signed.'
}

New-ToolboxReleaseManifest -Path $Path -Version $current.version | Out-Null
if ($certificate) {
    $signature = Add-ToolboxManifestSignature -Path $Path -Certificate $certificate
    Test-ToolboxRelease -Path $Path -TrustedThumbprint $signature.Sha1Thumbprint -RequireSignature | Out-Null
    Write-Host "Manifest for $($current.version) rebuilt and signed by $($signature.Subject)."
}
else {
    Test-ToolboxRelease -Path $Path | Out-Null
    Write-Host "Manifest for $($current.version) rebuilt (unsigned)."
}
