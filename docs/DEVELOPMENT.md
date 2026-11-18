# Development

## Build

The WinUI app builds on Windows only:

```powershell
msbuild Toolbox.sln /restore /p:Configuration=Debug /p:Platform=x64
```

Or use Visual Studio with the x64 platform. `global.json` selects the .NET 10 SDK.

`Toolbox.Core` and its tests build anywhere the .NET 10 SDK runs.

## Tests

```powershell
dotnet test tests/Toolbox.Core.Tests
```

These cover:

- settings layering, merging and validation
- LDAP escaping and filter building
- host name, executable and URL rules
- command-line quoting
- access policy
- LAPS parsing
- release manifests, signatures, staging, apply, rollback and recovery
- error messages and the log

None of the tests touch Active Directory or real credentials.

The PowerShell release module has Pester tests:

```powershell
Install-Module Pester -RequiredVersion 5.7.1 -Scope CurrentUser
Invoke-Pester tests/scripts
Invoke-ScriptAnalyzer -Path scripts -Recurse -Settings ./PSScriptAnalyzerSettings.psd1
```

`tests/Toolbox.Core.Tests/Fixtures/signed-release` was produced by `ToolboxRelease.psm1` with a throwaway certificate. The C# tests verify it, which keeps the two implementations compatible. `.gitattributes` keeps git from changing its line endings. If you regenerate it, update the thumbprint in `PowerShellReleaseCompatibilityTests`.

The app layer itself (WinUI, ADSI, registry, clipboard) has no automated tests. Check changes there by hand against a test domain.

## CI

| Workflow | Runs on | Does |
|----------|---------|------|
| `ci.yml` | push to main, pull requests | Core tests on Linux; Pester and PSScriptAnalyzer; Debug build, tests and an unpackaged publish on Windows |
| `codeql.yml` | push, pull requests, weekly | CodeQL for C# and for the workflows |
| `release.yml` | `v*` tags on main, manual | See [DEPLOYMENT.md](DEPLOYMENT.md) |
| `dependabot-auto-merge.yml` | Dependabot PRs | Auto-merges GitHub Actions minor/patch and NuGet patch updates once required checks pass |

All third-party actions are pinned to commit SHAs. Workflows default to read-only tokens, and only the release job can write.

Repository settings to turn on (they can't be set from files):

- Branch protection on `main` requiring the CI jobs. Auto-merge relies on this.
- Secret scanning and push protection.
- Dependabot security updates.

## Conventions

- Nullable reference types are on everywhere. `Toolbox.Core` and the tests treat warnings as errors.
- Never log or put into an exception message a password, a LAPS value or anything read from `msLAPS-*` / `ms-Mcs-AdmPwd`.
- Values from Active Directory or from settings are untrusted when they reach a command line, a file path or the shell. Go through `HostNameRules`, `ExecutableResolver`, `CommandTemplate`/`WindowsCommandLine` and `UrlPolicy`.
