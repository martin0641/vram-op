# VRAM Vue v6 Security Check

Date: 2026-06-05
Version: 6.0.0
Installer: `dist\VRAMVue-Setup-v6.0.0-win-x64.msi`

## Summary

No vulnerable or deprecated NuGet packages were reported by the current configured NuGet source. A lightweight secret scan found code-level password/token identifiers but no committed credentials or private keys. WiX MSI validation passed after correcting the Start Menu shortcut component scope.

## Commands Run

```powershell
dotnet list .\VramOp.csproj package --vulnerable --include-transitive
dotnet list .\VramOp.csproj package --deprecated
dotnet restore .\VramOp.csproj /p:NuGetAudit=true /p:NuGetAuditMode=all
dotnet list .\VramOp.csproj package --outdated
rg -n --hidden -S "(password\s*=|ProtectedPassword|BEGIN (RSA|OPENSSH|PRIVATE) KEY|ghp_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|AKIA[0-9A-Z]{16}|client_secret|api[_-]?key|token\s*=)" C:\git\vram-op -g "!bin/**" -g "!obj/**" -g "!.git/**"
.\scripts\Build-Msi.ps1 -Version 6.0.0
wix msi validate .\dist\VRAMVue-Setup-v6.0.0-win-x64.msi
```

## Results

- Vulnerable packages: none reported.
- Deprecated packages: none reported.
- NuGet audit restore: passed.
- Outdated packages: `System.Diagnostics.PerformanceCounter` and `System.Management` have newer `10.0.8` packages available. They were not upgraded in v6 because the current `8.0.0` packages reported no vulnerabilities and match the app's `net8.0-windows` target.
- Secret scan: no committed credentials or private keys found. Findings were expected code symbols such as `ProtectedPassword`, local variables named `password`, and cancellation-token variables.
- MSI validation: passed.

## Hardening Change

Freshly created listener certificates are now persisted as non-exportable current-user keys. Existing certificates are not rewritten automatically; deleting the old `VRAM Vue Local Telemetry` certificate from the current-user certificate store allows the app to create a new non-exportable one.

## Residual Risks

- First certificate pinning is trust-on-first-use. Use trusted networks and verify host/IP identity before entering credentials.
- Basic auth credentials are protected by TLS 1.3 in transit and DPAPI at rest, but should still be unique and strong.
- Authenticated remote clients can kill processes and control associated Windows services. Do not expose the listener port to untrusted networks.
