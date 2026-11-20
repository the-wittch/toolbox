#Requires -Version 5.1
<#
.SYNOPSIS
Publishes the unpackaged, self-contained Toolbox build and writes its release manifest.

.EXAMPLE
.\scripts\publish.ps1

.EXAMPLE
.\scripts\publish.ps1 -OutputDir \\fileserver\apps\Toolbox -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
#>
[CmdletBinding(DefaultParameterSetName = 'Unsigned')]
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist\Toolbox'),
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$ForceDotnet,
    [switch]$Clean,
    [Parameter(ParameterSetName = 'Store')][string]$CertificateThumbprint,
    [Parameter(ParameterSetName = 'Pfx')][string]$CertificatePath,
    [Parameter(ParameterSetName = 'Pfx')][SecureString]$CertificatePassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ToolboxRelease.psm1') -Force

$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\src\Toolbox\Toolbox.csproj')).ProviderPath
$projectDir = Split-Path -Parent $project
$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
$logFile = Join-Path $projectDir "publish-$Configuration.log"

[xml]$csproj = Get-Content -LiteralPath $project
$version = @($csproj.Project.PropertyGroup | ForEach-Object { $_.SelectSingleNode('Version') } | Where-Object { $_ })[0].InnerText
if ($version -notmatch '^\d+(\.\d+){1,3}$') { throw "Could not read a valid <Version> from $project." }

$certificate = $null
if ($PSCmdlet.ParameterSetName -eq 'Store') {
    $certificate = Get-ToolboxSigningCertificate -Thumbprint $CertificateThumbprint
}
elseif ($PSCmdlet.ParameterSetName -eq 'Pfx') {
    $certificate = Get-ToolboxSigningCertificate -PfxPath $CertificatePath -PfxPassword $CertificatePassword
}

function Get-VSMSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { return $null }
    $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null |
        Select-Object -First 1
    if ($found -and (Test-Path -LiteralPath $found)) { return $found }
    return $null
}

function Show-LogTail {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Write-Host ''
    Write-Host "----- last 60 lines of $Path -----"
    Get-Content -LiteralPath $Path -Tail 60
    Write-Host '----- errors -----'
    Select-String -LiteralPath $Path -Pattern ': error ' | Select-Object -Last 30 | ForEach-Object { $_.Line }
}

$objDir = Join-Path $projectDir 'obj'
if (Test-Path -LiteralPath $objDir) {
    Get-ChildItem -LiteralPath $objDir -Recurse -Filter 'Manifests' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
}

# leftovers from older builds would end up in the manifest
$defaultOutput = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\dist\Toolbox'))
if ((Test-Path -LiteralPath $OutputDir) -and (Get-ChildItem -LiteralPath $OutputDir -Force | Select-Object -First 1)) {
    if (-not $Clean -and $OutputDir.TrimEnd('\') -ne $defaultOutput.TrimEnd('\')) {
        throw "$OutputDir is not empty. Publish to an empty folder, or pass -Clean to delete its contents first."
    }
    Get-ChildItem -LiteralPath $OutputDir -Force | Remove-Item -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
if (Test-Path -LiteralPath $logFile) { Remove-Item -LiteralPath $logFile -Force }

$msbuild = Get-VSMSBuild
Write-Host "Publishing Toolbox $version ($Configuration) to $OutputDir"

if ($msbuild -and -not $ForceDotnet) {
    Write-Host "MSBuild: $msbuild"
    $msbuildArgs = @(
        $project, '/restore', '/t:Publish',
        "/p:Configuration=$Configuration",
        '/p:Platform=x64', '/p:PlatformTarget=x64', '/p:RuntimeIdentifier=win-x64',
        '/p:SelfContained=true', '/p:WindowsPackageType=None', '/p:WindowsAppSDKSelfContained=true',
        '/p:PublishSingleFile=false', '/p:PublishTrimmed=false', '/p:PublishReadyToRun=true',
        "/p:PublishDir=$OutputDir\", '/p:PublishProtocol=FileSystem',
        '/nologo', '/m', '/v:minimal', '/fl', "/flp:LogFile=$logFile;Verbosity=normal"
    )
    & $msbuild @msbuildArgs
    if ($LASTEXITCODE -ne 0) {
        Show-LogTail $logFile
        throw "MSBuild publish failed with exit code $LASTEXITCODE. Log: $logFile"
    }
}
else {
    if (-not $msbuild) { Write-Warning 'Visual Studio MSBuild not found; using dotnet publish.' }
    $dotnetArgs = @(
        'publish', $project, '-c', $Configuration, '-r', 'win-x64',
        '-p:Platform=x64', '-p:PlatformTarget=x64', '--self-contained', 'true',
        '-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=true',
        '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:PublishReadyToRun=true',
        '-o', $OutputDir, "-flp:LogFile=$logFile;Verbosity=normal"
    )
    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        Show-LogTail $logFile
        throw "dotnet publish failed with exit code $LASTEXITCODE. Log: $logFile"
    }
}

$exe = Join-Path $OutputDir 'Toolbox.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Show-LogTail $logFile
    throw "Publish finished but Toolbox.exe is missing from $OutputDir."
}

# unpackaged WinUI needs the resource index under both names, see microsoft/WindowsAppSDK#6720
$pri = Join-Path $OutputDir 'Toolbox.pri'
$resourcesPri = Join-Path $OutputDir 'resources.pri'
if (-not (Test-Path -LiteralPath $pri) -and -not (Test-Path -LiteralPath $resourcesPri)) {
    $found = Get-ChildItem -LiteralPath (Join-Path $projectDir "bin\x64\$Configuration") -Recurse -Include 'Toolbox.pri', 'resources.pri' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($found) {
        Write-Warning "PRI missing from publish output; copying $($found.FullName)"
        Copy-Item -LiteralPath $found.FullName -Destination $pri
    }
}
if ((Test-Path -LiteralPath $pri) -and -not (Test-Path -LiteralPath $resourcesPri)) { Copy-Item -LiteralPath $pri -Destination $resourcesPri }
if ((Test-Path -LiteralPath $resourcesPri) -and -not (Test-Path -LiteralPath $pri)) { Copy-Item -LiteralPath $resourcesPri -Destination $pri }
if (-not (Test-Path -LiteralPath $pri) -or -not (Test-Path -LiteralPath $resourcesPri)) {
    Show-LogTail $logFile
    throw "Toolbox.pri and resources.pri are both required in $OutputDir; without them the app fails with XamlParseException at startup."
}

foreach ($name in 'Install-Toolbox.ps1', 'ToolboxRelease.psm1', 'Update-ToolboxManifest.ps1') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $OutputDir $name) -Force
}

if ($certificate -and $certificate.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3') {
    foreach ($name in 'Install-Toolbox.ps1', 'ToolboxRelease.psm1', 'Update-ToolboxManifest.ps1') {
        $result = Set-AuthenticodeSignature -FilePath (Join-Path $OutputDir $name) -Certificate $certificate -TimestampServer $TimestampUrl -HashAlgorithm SHA256
        if ($result.Status -ne 'Valid') { throw "Signing $name failed: $($result.StatusMessage)" }
    }
}

New-ToolboxReleaseManifest -Path $OutputDir -Version $version | Out-Null
$signature = $null
if ($certificate) {
    $signature = Add-ToolboxManifestSignature -Path $OutputDir -Certificate $certificate
    $check = Test-ToolboxRelease -Path $OutputDir -TrustedThumbprint $signature.Sha1Thumbprint -RequireSignature
}
else {
    $check = Test-ToolboxRelease -Path $OutputDir
}

Write-Host ''
Write-Host "Published Toolbox $version to $OutputDir"
Write-Host "  Files in manifest: $($check.Files)"
if ($signature) {
    Write-Host "  Signed by:         $($signature.Subject)"
    Write-Host "  Pin this value in deployment.trustedSignerThumbprints (or the TrustedUpdateSigners policy):"
    Write-Host "    $($signature.Sha1Thumbprint)"
}
else {
    Write-Host '  Manifest is unsigned: clients check file hashes but cannot tell who published the release.'
}
Write-Host "  Build log:         $logFile"
Write-Host ''
Write-Host 'Copy the whole folder to the share. If you edit appsettings.json there afterwards, run'
Write-Host 'Update-ToolboxManifest.ps1 in that folder so installed copies accept the change.'
