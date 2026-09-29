#Requires -Version 5.1
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist\Toolbox'),
    [string]$Configuration = 'Release',
    [switch]$ForceDotnet
)

$ErrorActionPreference = 'Continue'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\src\Toolbox\Toolbox.csproj')).Path
$projectDir = Split-Path -Parent $project
$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
$logFile = Join-Path $projectDir "publish-$Configuration.log"

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

function Show-LogTail {
    param([string]$Path, [int]$Lines = 80)
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    Write-Host ""
    Write-Host "----- last $Lines lines of $Path -----"
    Get-Content -LiteralPath $Path -Tail $Lines
    Write-Host "----- end log -----"
    Write-Host ""
    Write-Host "Error lines:"
    Select-String -LiteralPath $Path -Pattern 'error |: error' | Select-Object -Last 30 | ForEach-Object { $_.Line }
}

$manifestRoot = Join-Path $projectDir 'obj'
if (Test-Path -LiteralPath $manifestRoot) {
    Get-ChildItem -LiteralPath $manifestRoot -Recurse -Filter 'Manifests' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object {
            Write-Host "Cleaning $($_.FullName)"
            Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
if (Test-Path -LiteralPath $logFile) {
    Remove-Item -LiteralPath $logFile -Force
}

$exitCode = 0
$msbuild = Get-VSMSBuild

if ($msbuild -and -not $ForceDotnet) {
    Write-Host "Using Visual Studio MSBuild:"
    Write-Host "  $msbuild"
    Write-Host "Log file:"
    Write-Host "  $logFile"
    Write-Host ""

    $args = @(
        $project,
        '/restore',
        '/t:Publish',
        "/p:Configuration=$Configuration",
        '/p:Platform=x64',
        '/p:PlatformTarget=x64',
        '/p:RuntimeIdentifier=win-x64',
        '/p:SelfContained=true',
        '/p:WindowsPackageType=None',
        '/p:WindowsAppSDKSelfContained=true',
        '/p:PublishSingleFile=false',
        '/p:PublishTrimmed=false',
        '/p:PublishReadyToRun=true',
        "/p:PublishDir=$OutputDir\",
        '/p:PublishProtocol=FileSystem',
        '/nologo',
        '/m',
        '/v:n',
        "/fl",
        "/flp:LogFile=$logFile;Verbosity=normal"
    )

    $proc = Start-Process -FilePath $msbuild -ArgumentList $args -Wait -PassThru -NoNewWindow
    $exitCode = $proc.ExitCode
    if ($exitCode -ne 0) {
        Show-LogTail -Path $logFile
        throw "MSBuild publish failed with exit code $exitCode. See log: $logFile"
    }
}
else {
    if (-not $msbuild) {
        Write-Warning "Visual Studio MSBuild not found; falling back to dotnet publish (more likely to hit XAML compiler crashes)."
    }
    else {
        Write-Host "ForceDotnet specified; using dotnet publish."
    }

    Write-Host "Log file:"
    Write-Host "  $logFile"
    Write-Host ""

    $dotnetArgs = @(
        'publish', $project,
        '-c', $Configuration,
        '-r', 'win-x64',
        '-p:Platform=x64',
        '-p:PlatformTarget=x64',
        '--self-contained', 'true',
        '-p:WindowsPackageType=None',
        '-p:WindowsAppSDKSelfContained=true',
        '-p:PublishSingleFile=false',
        '-p:PublishTrimmed=false',
        '-p:PublishReadyToRun=true',
        '-o', $OutputDir
    )

    & dotnet @dotnetArgs 2>&1 | Tee-Object -FilePath $logFile
    $exitCode = $LASTEXITCODE
    if ($null -eq $exitCode) { $exitCode = 1 }
    if ($exitCode -ne 0) {
        Show-LogTail -Path $logFile
        throw "dotnet publish failed with exit code $exitCode. See log: $logFile"
    }
}

$installer = Join-Path $PSScriptRoot 'Install-Toolbox.ps1'
if (Test-Path -LiteralPath $installer) {
    Copy-Item -LiteralPath $installer -Destination (Join-Path $OutputDir 'Install-Toolbox.ps1') -Force
}

$exe = Join-Path $OutputDir 'Toolbox.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Show-LogTail -Path $logFile
    throw "Publish finished but Toolbox.exe was not found in '$OutputDir'. See log: $logFile"
}

$pri = Join-Path $OutputDir 'Toolbox.pri'
$resourcesPri = Join-Path $OutputDir 'resources.pri'

function Find-BuildPri {
    $candidates = @(
        (Join-Path $projectDir "bin\x64\$Configuration\net8.0-windows10.0.19041.0\win-x64\Toolbox.pri"),
        (Join-Path $projectDir "bin\x64\$Configuration\net8.0-windows10.0.19041.0\win-x64\resources.pri"),
        (Join-Path $projectDir "bin\x64\$Configuration\net8.0-windows10.0.19041.0\Toolbox.pri"),
        (Join-Path $projectDir "bin\x64\$Configuration\net8.0-windows10.0.19041.0\resources.pri")
    )
    return $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if (-not (Test-Path -LiteralPath $pri) -and -not (Test-Path -LiteralPath $resourcesPri)) {
    $found = Find-BuildPri
    if ($found) {
        Write-Warning "PRI missing from publish output; copying from build output: $found"
        Copy-Item -LiteralPath $found -Destination $pri -Force
        Copy-Item -LiteralPath $found -Destination $resourcesPri -Force
    }
}

if ((Test-Path -LiteralPath $pri) -and -not (Test-Path -LiteralPath $resourcesPri)) {
    Copy-Item -LiteralPath $pri -Destination $resourcesPri -Force
    Write-Host "Mirrored Toolbox.pri → resources.pri"
}

if ((Test-Path -LiteralPath $resourcesPri) -and -not (Test-Path -LiteralPath $pri)) {
    Copy-Item -LiteralPath $resourcesPri -Destination $pri -Force
    Write-Host "Mirrored resources.pri → Toolbox.pri"
}

if (-not (Test-Path -LiteralPath $pri) -or -not (Test-Path -LiteralPath $resourcesPri)) {
    Show-LogTail -Path $logFile
    throw @"
Publish finished but PRI files are missing from '$OutputDir'.
Need both: Toolbox.pri and resources.pri
Unpackaged WinUI will crash at MainWindow with XamlParseException.
Rebuild with the repo Directory.Build.targets, or copy *.pri from bin\ into the publish folder.
See: https://github.com/microsoft/WindowsAppSDK/issues/6720
Log: $logFile
"@
}

$xbfCount = @(Get-ChildItem -LiteralPath $OutputDir -Filter '*.xbf' -Recurse -ErrorAction SilentlyContinue).Count
Write-Host ""
Write-Host "Published unpackaged package to: $OutputDir"
Write-Host "Entry point: $exe"
Write-Host "App PRI:     $pri"
Write-Host "Resources:   $resourcesPri"
Write-Host "XBF files:   $xbfCount"
Write-Host "Installer:   $(Join-Path $OutputDir 'Install-Toolbox.ps1')"
Write-Host "Settings:    $(Join-Path $OutputDir 'appsettings.json')"
Write-Host "Build log:   $logFile"
Write-Host ""
Write-Host "Copy the entire folder to your file share. Preserve a customized appsettings.json when updating."
Write-Host "Staff install (no admin): right-click Install-Toolbox.ps1 -> Run with PowerShell"
Write-Host "If staff still crash: confirm LocalAppData\Programs\Toolbox has Toolbox.pri and resources.pri"
