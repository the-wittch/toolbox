BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..\..\scripts\ToolboxRelease.psm1') -Force

    function New-TestRelease {
        $root = Join-Path ([System.IO.Path]::GetTempPath()) ("toolbox-release-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $root 'Assets') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $root 'Toolbox.exe') -Value 'exe' -NoNewline
        Set-Content -LiteralPath (Join-Path $root 'Toolbox.dll') -Value 'dll' -NoNewline
        Set-Content -LiteralPath (Join-Path $root 'Assets/AppIcon.ico') -Value 'icon' -NoNewline
        Set-Content -LiteralPath (Join-Path $root 'Install-Toolbox.ps1') -Value '# installer' -NoNewline
        return $root
    }

    function New-TestCertificate {
        $rsa = [System.Security.Cryptography.RSA]::Create(2048)
        $request = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest('CN=Pester Signing', $rsa,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        return $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(1))
    }
}

Describe 'New-ToolboxReleaseManifest' {
    BeforeEach { $release = New-TestRelease }
    AfterEach { Remove-Item -LiteralPath $release -Recurse -Force }

    It 'lists release files with forward slashes and skips installer scripts' {
        New-ToolboxReleaseManifest -Path $release -Version '3.1.0' | Out-Null
        $manifest = Get-Content -LiteralPath (Join-Path $release 'toolbox-manifest.json') -Raw | ConvertFrom-Json
        $manifest.version | Should -Be '3.1.0'
        $manifest.files.path | Should -Contain 'Assets/AppIcon.ico'
        $manifest.files.path | Should -Not -Contain 'Install-Toolbox.ps1'
    }

    It 'refuses a folder without Toolbox.exe' {
        Remove-Item -LiteralPath (Join-Path $release 'Toolbox.exe')
        { New-ToolboxReleaseManifest -Path $release -Version '3.1.0' } | Should -Throw '*Toolbox.exe*'
    }

    It 'rejects an invalid version' {
        { New-ToolboxReleaseManifest -Path $release -Version 'latest' } | Should -Throw
    }
}

Describe 'Test-ToolboxRelease' {
    BeforeEach {
        $release = New-TestRelease
        New-ToolboxReleaseManifest -Path $release -Version '3.1.0' | Out-Null
        $cert = New-TestCertificate
        $signature = Add-ToolboxManifestSignature -Path $release -Certificate $cert
    }
    AfterEach { Remove-Item -LiteralPath $release -Recurse -Force }

    It 'accepts a signed release with the pinned thumbprint' {
        $result = Test-ToolboxRelease -Path $release -TrustedThumbprint $signature.Sha1Thumbprint -RequireSignature
        $result.Signed | Should -BeTrue
        $result.Files | Should -Be 3
    }

    It 'accepts the SHA-256 thumbprint as a pin' {
        (Test-ToolboxRelease -Path $release -TrustedThumbprint $signature.Sha256Thumbprint).Signed | Should -BeTrue
    }

    It 'rejects a release signed by another certificate' {
        { Test-ToolboxRelease -Path $release -TrustedThumbprint ('A' * 40) } | Should -Throw '*untrusted*'
    }

    It 'rejects a modified file' {
        Set-Content -LiteralPath (Join-Path $release 'Toolbox.dll') -Value 'evil' -NoNewline
        { Test-ToolboxRelease -Path $release -TrustedThumbprint $signature.Sha1Thumbprint } | Should -Throw '*Toolbox.dll*'
    }

    It 'rejects a modified manifest' {
        $path = Join-Path $release 'toolbox-manifest.json'
        (Get-Content -LiteralPath $path -Raw).Replace('3.1.0', '9.9.9') | Set-Content -LiteralPath $path -NoNewline
        { Test-ToolboxRelease -Path $release -TrustedThumbprint $signature.Sha1Thumbprint } | Should -Throw '*does not match*'
    }

    It 'rejects a missing signature when a pin is configured' {
        Remove-Item -LiteralPath (Join-Path $release 'toolbox-manifest.sig')
        { Test-ToolboxRelease -Path $release -TrustedThumbprint $signature.Sha1Thumbprint } | Should -Throw '*not signed*'
    }

    It 'requires a pin when a signature is required' {
        { Test-ToolboxRelease -Path $release -RequireSignature } | Should -Throw
    }

    It 'rebuilding the manifest drops the old signature' {
        New-ToolboxReleaseManifest -Path $release -Version '3.1.0' | Out-Null
        Test-Path -LiteralPath (Join-Path $release 'toolbox-manifest.sig') | Should -BeFalse
    }
}
