# VRAM Vue v6 Security Check

Date: 2026-06-06
Version: 6.0.4
Installer: `dist\VRAMVue-Setup-v6.0.4-win-x64.msi`
Portable: `dist\VRAMVue-Portable-v6.0.4-win-x64.zip`

## Summary

No vulnerable or deprecated NuGet packages were reported by the current configured NuGet source. A lightweight secret scan found code-level password/token identifiers but no committed credentials or private keys. WiX MSI validation passed. The distribution build produces both a self-contained MSI and a self-contained portable zip, settings exports use password-based AES-256-GCM encryption, and network activity telemetry is collected without adding new third-party dependencies.

## Local Smoke Artifact Hashes

```text
VRAMVue-Setup-v6.0.4-win-x64.msi     F4EC157DA46B3C6EC28E10FC45DE979C5B1ECA89C45CF2E236C4B22302718FF0
VRAMVue-Portable-v6.0.4-win-x64.zip  3958DE7D91011071C66B60BB650D36276FBB9C8F358CD755BF816263D01928CD
```

## Commands Run

```powershell
dotnet list .\VramOp.csproj package --vulnerable --include-transitive
dotnet list .\VramOp.csproj package --deprecated
dotnet restore .\VramOp.csproj /p:NuGetAudit=true /p:NuGetAuditMode=all
dotnet list .\VramOp.csproj package --outdated
rg -n --hidden -S "(password\s*=|ProtectedPassword|BEGIN (RSA|OPENSSH|PRIVATE) KEY|ghp_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|AKIA[0-9A-Z]{16}|client_secret|api[_-]?key|token\s*=)" . -g "!bin/**" -g "!obj/**" -g "!artifacts/**" -g "!dist/**" -g "!**/.git/**"
.\scripts\Build-Msi.ps1 -Version 6.0.4
wix msi validate .\dist\VRAMVue-Setup-v6.0.4-win-x64.msi
```

## Results

- Vulnerable packages: none reported.
- Deprecated packages: none reported.
- NuGet audit restore: passed.
- Outdated packages: `System.Diagnostics.PerformanceCounter` and `System.Management` have newer `10.0.8` packages available. They were not upgraded in v6 because the current `8.0.0` packages reported no vulnerabilities and match the app's `net8.0-windows` target.
- Secret scan: no committed credentials or private keys found. Findings were expected code symbols such as `ProtectedPassword`, local variables named `password`, and cancellation-token variables.
- MSI validation: passed.
- Portable zip build: passed.

## Hardening Changes

- Freshly created listener certificates are persisted as non-exportable current-user keys. Existing certificates are not rewritten automatically; deleting the old `VRAM Vue Local Telemetry` certificate from the current-user certificate store allows the app to create a new non-exportable one.
- Settings exports decrypt local DPAPI-protected credentials in memory, then write a portable settings file encrypted with PBKDF2-SHA256 and AES-256-GCM.

## Residual Risks

- First certificate pinning is trust-on-first-use. Use trusted networks and verify host/IP identity before entering credentials.
- Basic auth credentials are protected by TLS 1.3 in transit and DPAPI at rest, but should still be unique and strong.
- Exported settings are only as strong as the chosen export password.
- Authenticated remote clients can kill processes and control associated Windows services. Do not expose the listener port to untrusted networks.

## Maximus Runtime Diagnosis

On `Maximus` (`192.168.1.2`), a copied framework-dependent developer build at `C:\net8.0-windows\VramVue.exe` failed before opening. The Application log reported `.NET Runtime` error 1023 against the target app, and inspection showed:

- `Microsoft.WindowsDesktop.App 8.x` was not installed.
- `C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.14\Microsoft.NETCore.App.runtimeconfig.json` contained NUL bytes and was not valid JSON.

The MSI and portable zip avoid this class of failure because they are self-contained win-x64 distribution builds.
