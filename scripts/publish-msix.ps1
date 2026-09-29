#Requires -Version 5.1
param(
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64',
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist\msix'),
    [string]$CertificatePath = '',
    [SecureString]$CertificatePassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$ForceDotnet
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src\Toolbox\Toolbox.csproj'
$manifest = Join-Path $repoRoot 'src\Toolbox\Package.appxmanifest'
$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
$logFile = Join-Path $repoRoot "src\Toolbox\publish-msix-$Configuration.log"

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}
if (-not (Test-Path -LiteralPath $manifest)) {
    throw "Package.appxmanifest not found: $manifest"
}

function Get-VSMSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        return $null
    }

    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
        -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null |
        Select-Object -First 1

    if ($msbuild -and (Test-Path -LiteralPath $msbuild)) {
        return $msbuild
    }

    return $null
}

function Get-SignTool {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $fromVs = & $vswhere -latest -products * -find '**\signtool.exe' 2>$null |
            Where-Object { $_ -match '\\x64\\' } |
            Select-Object -First 1
        if ($fromVs -and (Test-Path -LiteralPath $fromVs)) {
            return $fromVs
        }
    }

    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kits) {
        $kitTool = Get-ChildItem -LiteralPath $kits -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($kitTool) {
            return $kitTool.FullName
        }
    }

    return $null
}

function Show-LogTail {
    param([string]$Path, [int]$Lines = 80)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Write-Host ""
    Write-Host "----- last $Lines lines of $Path -----"
    Get-Content -LiteralPath $Path -Tail $Lines
    Write-Host "----- end log -----"
}

$appPackages = Join-Path $repoRoot 'src\Toolbox\AppPackages'
if (Test-Path -LiteralPath $appPackages) {
    Write-Host "Cleaning $appPackages"
    Remove-Item -LiteralPath $appPackages -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
if (Test-Path -LiteralPath $logFile) {
    Remove-Item -LiteralPath $logFile -Force
}

Write-Host "Building MSIX package"
Write-Host "  Project:  $project"
Write-Host "  Output:   $OutputDir"
Write-Host "  Manifest: $manifest"
Write-Host ""

$msbuild = Get-VSMSBuild
$exitCode = 0

if ($msbuild -and -not $ForceDotnet) {
    Write-Host "Using Visual Studio MSBuild:"
    Write-Host "  $msbuild"
    Write-Host "Log:"
    Write-Host "  $logFile"
    Write-Host ""

    $args = @(
        $project,
        '/restore',
        '/t:Publish',
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        '/p:PlatformTarget=x64',
        '/p:RuntimeIdentifier=win-x64',
        '/p:SelfContained=true',
        '/p:WindowsPackageType=MSIX',
        '/p:EnableMsixTooling=true',
        '/p:GenerateAppxPackageOnBuild=true',
        '/p:AppxPackageSigningEnabled=false',
        '/p:AppxBundle=Never',
        '/p:UapAppxPackageBuildMode=SideLoadOnly',
        '/p:WindowsAppSDKSelfContained=true',
        '/p:PublishSingleFile=false',
        '/p:PublishTrimmed=false',
        '/p:PublishReadyToRun=true',
        '/nologo',
        '/m',
        '/v:n',
        '/fl',
        "/flp:LogFile=$logFile;Verbosity=normal"
    )

    $proc = Start-Process -FilePath $msbuild -ArgumentList $args -Wait -PassThru -NoNewWindow
    $exitCode = $proc.ExitCode
}
else {
    if (-not $msbuild) {
        Write-Warning "Visual Studio MSBuild not found; trying dotnet build (MSIX generation is less reliable)."
    }

    & dotnet publish $project `
        -c $Configuration `
        -r win-x64 `
        -p:Platform=x64 `
        -p:PlatformTarget=x64 `
        --self-contained true `
        -p:WindowsPackageType=MSIX `
        -p:EnableMsixTooling=true `
        -p:GenerateAppxPackageOnBuild=true `
        -p:AppxPackageSigningEnabled=false `
        -p:AppxBundle=Never `
        -p:UapAppxPackageBuildMode=SideLoadOnly `
        -p:WindowsAppSDKSelfContained=true `
        2>&1 | Tee-Object -FilePath $logFile
    $exitCode = $LASTEXITCODE
    if ($null -eq $exitCode) { $exitCode = 1 }
}

if ($exitCode -ne 0) {
    Show-LogTail -Path $logFile
    throw "MSIX build failed with exit code $exitCode. See log: $logFile"
}

$msix = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src\Toolbox') -Recurse -Filter '*.msix' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\AppPackages\\' -or $_.DirectoryName -match 'msix' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msix) {
    $msix = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src\Toolbox') -Recurse -Filter '*.msix' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

if (-not $msix) {
    Show-LogTail -Path $logFile
    throw "Build succeeded but no .msix was found under src\Toolbox. Open the log and confirm GenerateAppxPackageOnBuild ran: $logFile"
}

$destMsix = Join-Path $OutputDir $msix.Name
Copy-Item -LiteralPath $msix.FullName -Destination $destMsix -Force
Write-Host ""
Write-Host "MSIX copied to: $destMsix"

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    if (-not (Test-Path -LiteralPath $CertificatePath)) {
        throw "Certificate not found: $CertificatePath"
    }

    $signTool = Get-SignTool
    if (-not $signTool) {
        throw "signtool.exe not found. Install the Windows SDK or use Visual Studio Desktop development workload."
    }

    Write-Host "Signing with:"
    Write-Host "  $signTool"
    Write-Host "  Cert: $CertificatePath"

    $plainPassword = $null
    if ($CertificatePassword) {
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword)
        try {
            $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }

    $signArgs = @(
        'sign',
        '/fd', 'SHA256',
        '/tr', $TimestampUrl,
        '/td', 'SHA256',
        '/f', $CertificatePath,
        $destMsix
    )
    if ($plainPassword) {
        $signArgs = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', '/f', $CertificatePath, '/p', $plainPassword, $destMsix)
    }

    & $signTool @signArgs
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed with exit code $LASTEXITCODE. Confirm Package.appxmanifest Publisher matches the cert Subject."
    }

    Write-Host "Signed successfully."
}
else {
    Write-Host ""
    Write-Host "Package is UNSIGNED. On your Windows PC, sign it before SCCM:"
    Write-Host "  signtool sign /fd SHA256 /tr $TimestampUrl /td SHA256 /f YourCert.pfx /p <pwd> `"$destMsix`""
    Write-Host ""
    Write-Host "Publisher in Package.appxmanifest must match the certificate Subject exactly."
}

Write-Host ""
Write-Host "Next (SCCM):"
Write-Host "  1. Trust the signing cert on clients (GPO / Cert Profile) if needed"
Write-Host "  2. Create Application → Windows app package (*.msix) → select the signed file"
Write-Host "  3. Deploy to help-desk collection; bump Identity Version for upgrades"
Write-Host "  4. Keep unpackaged publish.ps1 only if you still need the file-share path"
Write-Host ""
Write-Host "Build log: $logFile"
Write-Host "Package:   $destMsix"
