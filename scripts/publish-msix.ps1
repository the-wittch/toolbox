#Requires -Version 5.1
<#
.SYNOPSIS
Builds the Toolbox MSIX package for deployment with Configuration Manager or Intune.

.DESCRIPTION
Without a certificate the package is left unsigned. With -CertificateThumbprint the package
is signed from the certificate store (smart cards and HSM-backed keys work). With
-CertificatePath the PFX is imported into CurrentUser\My for the duration of the signing so its
password never appears on a command line.

.EXAMPLE
.\scripts\publish-msix.ps1 -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
#>
[CmdletBinding(DefaultParameterSetName = 'Unsigned')]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist\msix'),
    [Parameter(ParameterSetName = 'Store')][string]$CertificateThumbprint,
    [Parameter(ParameterSetName = 'Pfx')][string]$CertificatePath,
    [Parameter(ParameterSetName = 'Pfx')][SecureString]$CertificatePassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$ForceDotnet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$projectDir = Join-Path $repoRoot 'src\Toolbox'
$project = Join-Path $projectDir 'Toolbox.csproj'
$manifest = Join-Path $projectDir 'Package.appxmanifest'
$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
$logFile = Join-Path $projectDir "publish-msix-$Configuration.log"

foreach ($required in $project, $manifest) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Not found: $required" }
}

function Get-VSMSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { return $null }
    $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null |
        Select-Object -First 1
    if ($found -and (Test-Path -LiteralPath $found)) { return $found }
    return $null
}

function Get-SignTool {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kits) {
        $tool = Get-ChildItem -LiteralPath $kits -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($tool) { return $tool.FullName }
    }
    $nuget = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
    if (Test-Path -LiteralPath $nuget) {
        $tool = Get-ChildItem -LiteralPath $nuget -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($tool) { return $tool.FullName }
    }
    return $null
}

function Show-LogTail {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Write-Host "----- last 60 lines of $Path -----"
    Get-Content -LiteralPath $Path -Tail 60
}

$appPackages = Join-Path $projectDir 'AppPackages'
if (Test-Path -LiteralPath $appPackages) { Remove-Item -LiteralPath $appPackages -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
if (Test-Path -LiteralPath $logFile) { Remove-Item -LiteralPath $logFile -Force }

$common = @(
    "-p:Configuration=$Configuration", '-p:Platform=x64', '-p:PlatformTarget=x64', '-p:RuntimeIdentifier=win-x64',
    '-p:SelfContained=true', '-p:WindowsPackageType=MSIX', '-p:EnableMsixTooling=true',
    '-p:GenerateAppxPackageOnBuild=true', '-p:AppxPackageSigningEnabled=false', '-p:AppxBundle=Never',
    '-p:UapAppxPackageBuildMode=SideLoadOnly', '-p:WindowsAppSDKSelfContained=true',
    "-p:AppxPackageDir=$appPackages\"
)

$msbuild = Get-VSMSBuild
Write-Host "Building MSIX ($Configuration)"
if ($msbuild -and -not $ForceDotnet) {
    & $msbuild $project '/restore' '/t:Publish' @common '/p:PublishReadyToRun=true' '/nologo' '/m' '/v:minimal' '/fl' "/flp:LogFile=$logFile;Verbosity=normal"
}
else {
    if (-not $msbuild) { Write-Warning 'Visual Studio MSBuild not found; using dotnet publish (less reliable for MSIX).' }
    & dotnet publish $project @common "-flp:LogFile=$logFile;Verbosity=normal"
}
if ($LASTEXITCODE -ne 0) {
    Show-LogTail $logFile
    throw "MSIX build failed with exit code $LASTEXITCODE. Log: $logFile"
}

$msix = @(Get-ChildItem -LiteralPath $appPackages -Recurse -Filter '*.msix' -ErrorAction SilentlyContinue)
if ($msix.Count -ne 1) {
    Show-LogTail $logFile
    throw "Expected exactly one .msix under $appPackages, found $($msix.Count)."
}

$destMsix = Join-Path $OutputDir $msix[0].Name
Copy-Item -LiteralPath $msix[0].FullName -Destination $destMsix -Force
Write-Host "Package: $destMsix"

if ($PSCmdlet.ParameterSetName -ne 'Unsigned') {
    $signTool = Get-SignTool
    if (-not $signTool) { throw 'signtool.exe not found. Install the Windows SDK.' }

    $imported = $null
    try {
        if ($PSCmdlet.ParameterSetName -eq 'Pfx') {
            if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) { throw "Certificate not found: $CertificatePath" }
            $pfxArgs = @{ FilePath = $CertificatePath }
            if ($CertificatePassword) { $pfxArgs.Password = $CertificatePassword }
            $pfxThumb = (Get-PfxData @pfxArgs).EndEntityCertificates[0].Thumbprint
            $alreadyPresent = Test-Path -LiteralPath "Cert:\CurrentUser\My\$pfxThumb"
            $importArgs = $pfxArgs + @{ CertStoreLocation = 'Cert:\CurrentUser\My' }
            $certificateFromPfx = Import-PfxCertificate @importArgs
            if (-not $alreadyPresent) { $imported = $certificateFromPfx }
            $CertificateThumbprint = $certificateFromPfx.Thumbprint
        }

        $thumb = ($CertificateThumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
        [xml]$appx = Get-Content -LiteralPath $manifest
        $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My | Where-Object Thumbprint -eq $thumb | Select-Object -First 1
        if (-not $cert) { throw "Certificate $thumb is not in the certificate store." }
        if ($cert.Subject -ne $appx.Package.Identity.Publisher) {
            throw "Package.appxmanifest Publisher '$($appx.Package.Identity.Publisher)' must match the certificate subject '$($cert.Subject)'."
        }

        & $signTool sign /fd SHA256 /tr $TimestampUrl /td SHA256 /sha1 $thumb $destMsix
        if ($LASTEXITCODE -ne 0) { throw "signtool sign failed with exit code $LASTEXITCODE." }
        & $signTool verify /pa $destMsix
        if ($LASTEXITCODE -ne 0) { throw 'signtool verify failed. Is the signing certificate trusted on this machine?' }
        Write-Host "Signed by $($cert.Subject)"
    }
    finally {
        if ($imported) { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($imported.Thumbprint)" -ErrorAction SilentlyContinue }
    }
}
else {
    Write-Host 'Package is unsigned. Sign it before deploying:'
    Write-Host "  .\scripts\publish-msix.ps1 -CertificateThumbprint <thumbprint>"
}

$hash = (Get-FileHash -LiteralPath $destMsix -Algorithm SHA256).Hash
Set-Content -LiteralPath "$destMsix.sha256" -Value "$hash  $(Split-Path -Leaf $destMsix)" -Encoding ASCII
Write-Host "SHA-256: $hash"
Write-Host "Build log: $logFile"
