#Requires -Version 5.1
Set-StrictMode -Version Latest

$script:ManifestName = 'toolbox-manifest.json'
$script:SignatureName = 'toolbox-manifest.sig'
$script:InstallInfoName = 'toolbox-install.json'
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# not part of a release: installer scripts, the manifest itself and per-install state
$script:ExcludedNames = @(
    $script:ManifestName, $script:SignatureName, $script:InstallInfoName,
    'Install-Toolbox.ps1', 'ToolboxRelease.psm1', 'Update-ToolboxManifest.ps1', 'SHA256SUMS.txt'
)

function Get-RelativeReleasePath {
    param([string]$Root, [string]$FullName)
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $relative = $FullName.Substring($rootFull.Length).TrimStart('\', '/')
    return $relative.Replace('\', '/')
}

function Test-SafeReleasePath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.StartsWith('/') -or $Path.StartsWith('\')) { return $false }
    foreach ($segment in $Path.Split([char[]]'/\')) {
        if ($segment -eq '' -or $segment -eq '.' -or $segment -eq '..') { return $false }
        if ($segment.IndexOfAny([char[]]'<>:"|?*') -ge 0) { return $false }
        if ($segment.EndsWith('.') -or $segment.EndsWith(' ')) { return $false }
        if ($segment.Contains('~')) { return $false }
    }
    if ($Path.Split([char[]]'/\')[0] -eq '.toolbox-update') { return $false }
    return $true
}

function Get-Sha256Hex {
    param([byte[]]$Bytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '') }
    finally { $sha.Dispose() }
}

function ConvertTo-NormalizedThumbprint {
    param([string]$Value)
    return (($Value -replace '[^0-9A-Fa-f]', '').ToUpperInvariant())
}

function Get-ToolboxSigningCertificate {
    <#
    .SYNOPSIS
    Loads a code-signing certificate with its private key, from the certificate store or a PFX file.
    #>
    [CmdletBinding(DefaultParameterSetName = 'Store')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'Store')][string]$Thumbprint,
        [Parameter(Mandatory, ParameterSetName = 'Pfx')][string]$PfxPath,
        [Parameter(ParameterSetName = 'Pfx')][SecureString]$PfxPassword
    )

    if ($PSCmdlet.ParameterSetName -eq 'Store') {
        $clean = ConvertTo-NormalizedThumbprint $Thumbprint
        $cert = Get-ChildItem -Path Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $clean } | Select-Object -First 1
        if (-not $cert) { throw "Certificate $clean was not found in CurrentUser\My or LocalMachine\My." }
    }
    else {
        if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) { throw "PFX file not found: $PfxPath" }
        $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
        if ($PfxPassword) {
            $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2((Resolve-Path -LiteralPath $PfxPath).ProviderPath, $PfxPassword, $flags)
        }
        else {
            $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2((Resolve-Path -LiteralPath $PfxPath).ProviderPath, [string]$null, $flags)
        }
    }

    if (-not $cert.HasPrivateKey) { throw "Certificate $($cert.Thumbprint) has no private key available." }
    return $cert
}

function New-ToolboxReleaseManifest {
    <#
    .SYNOPSIS
    Writes toolbox-manifest.json listing every file in a published Toolbox folder with its size and SHA-256.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][ValidatePattern('^\d+(\.\d+){1,3}$')][string]$Version
    )

    $root = (Resolve-Path -LiteralPath $Path).ProviderPath
    $files = New-Object System.Collections.Generic.List[object]
    Get-ChildItem -LiteralPath $root -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
        $relative = Get-RelativeReleasePath -Root $root -FullName $_.FullName
        if ($script:ExcludedNames -contains $relative) { return }
        if ($relative -like '.toolbox-update/*' -or $relative -like '*.toolbox-old*') { return }
        if (-not (Test-SafeReleasePath $relative)) { throw "File name not allowed in a release: $relative" }
        $files.Add([ordered]@{
            path   = $relative
            size   = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        })
    }

    if (-not ($files | Where-Object { $_.path -eq 'Toolbox.exe' })) {
        throw "Toolbox.exe was not found in $root."
    }

    $manifest = [ordered]@{
        formatVersion = 1
        product       = 'Toolbox'
        version       = $Version
        files         = $files.ToArray()
    }

    $target = Join-Path $root $script:ManifestName
    if (-not $PSCmdlet.ShouldProcess($target, 'Write release manifest')) { return $target }
    [System.IO.File]::WriteAllText($target, ($manifest | ConvertTo-Json -Depth 5), $script:Utf8NoBom)
    $signature = Join-Path $root $script:SignatureName
    if (Test-Path -LiteralPath $signature) { Remove-Item -LiteralPath $signature -Force }
    Write-Verbose "Wrote $target with $($files.Count) files."
    return $target
}

function Add-ToolboxManifestSignature {
    <#
    .SYNOPSIS
    Signs toolbox-manifest.json (RSA, SHA-256, PKCS#1 v1.5) and writes toolbox-manifest.sig next to it.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    $root = (Resolve-Path -LiteralPath $Path).ProviderPath
    $manifestPath = Join-Path $root $script:ManifestName
    $bytes = [System.IO.File]::ReadAllBytes($manifestPath)

    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Certificate)
    if (-not $rsa) { throw 'The signing certificate must have an RSA key.' }
    $signature = $rsa.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

    $document = [ordered]@{
        algorithm   = 'RSA-PKCS1-SHA256'
        certificate = [Convert]::ToBase64String($Certificate.RawData)
        signature   = [Convert]::ToBase64String($signature)
    }
    $target = Join-Path $root $script:SignatureName
    [System.IO.File]::WriteAllText($target, ($document | ConvertTo-Json), $script:Utf8NoBom)

    [pscustomobject]@{
        Signature         = $target
        Subject           = $Certificate.Subject
        Sha1Thumbprint    = $Certificate.Thumbprint
        Sha256Thumbprint  = Get-Sha256Hex $Certificate.RawData
    }
}

function Test-ToolboxRelease {
    <#
    .SYNOPSIS
    Checks a release folder the same way Toolbox does before installing it: signature (when
    thumbprints are given) and the size and SHA-256 of every listed file.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [string[]]$TrustedThumbprint = @(),
        [switch]$RequireSignature
    )

    $root = (Resolve-Path -LiteralPath $Path).ProviderPath
    $manifestPath = Join-Path $root $script:ManifestName
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "No $($script:ManifestName) in $root. Republish with scripts\publish.ps1."
    }

    $bytes = [System.IO.File]::ReadAllBytes($manifestPath)
    $pins = @($TrustedThumbprint | ForEach-Object { ConvertTo-NormalizedThumbprint $_ } | Where-Object { $_ })
    if ($RequireSignature -and $pins.Count -eq 0) { throw 'A signature is required but no trusted thumbprint was given.' }

    $signer = $null
    if ($pins.Count -gt 0) {
        $sigPath = Join-Path $root $script:SignatureName
        if (-not (Test-Path -LiteralPath $sigPath -PathType Leaf)) { throw "The release is not signed ($($script:SignatureName) is missing)." }
        $sig = Get-Content -LiteralPath $sigPath -Raw | ConvertFrom-Json
        if ($sig.algorithm -ne 'RSA-PKCS1-SHA256') { throw 'Unsupported signature algorithm.' }

        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(, [Convert]::FromBase64String($sig.certificate))
        $sha1 = ConvertTo-NormalizedThumbprint $cert.Thumbprint
        $sha256 = Get-Sha256Hex $cert.RawData
        if (($pins -notcontains $sha1) -and ($pins -notcontains $sha256)) {
            throw "The release was signed by an untrusted certificate ($($cert.Subject), $sha1)."
        }

        $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($cert)
        $valid = $rsa.VerifyData($bytes, [Convert]::FromBase64String($sig.signature),
            [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        if (-not $valid) { throw 'The release signature does not match the manifest. The release may have been modified.' }
        $signer = $cert.Subject
    }

    $manifest = $script:Utf8NoBom.GetString($bytes) | ConvertFrom-Json
    if ($manifest.formatVersion -ne 1 -or $manifest.product -ne 'Toolbox') { throw 'This is not a Toolbox release manifest.' }

    foreach ($file in $manifest.files) {
        if (-not (Test-SafeReleasePath $file.path)) { throw "Manifest entry not allowed: $($file.path)" }
        $full = Join-Path $root ($file.path.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Missing from the release folder: $($file.path)" }
        $item = Get-Item -LiteralPath $full
        if ($item.Length -ne [long]$file.size -or (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ne $file.sha256.ToUpperInvariant()) {
            throw "Integrity check failed for $($file.path)."
        }
    }

    [pscustomobject]@{
        Path     = $root
        Version  = $manifest.version
        Files    = @($manifest.files).Count
        Signed   = [bool]$signer
        Signer   = $signer
        Manifest = $manifest
    }
}

Export-ModuleMember -Function Get-ToolboxSigningCertificate, New-ToolboxReleaseManifest, Add-ToolboxManifestSignature, Test-ToolboxRelease
