# VRAM Vue v6 Security Check

Date: 2026-06-05
Version: 6.0.0
Installer: `dist\VRAMVue-Setup-v6.0.0-win-x64.msi`
Portable: `dist\VRAMVue-Portable-v6.0.0-win-x64.zip`

## Summary

No vulnerable or deprecated NuGet packages were reported by the current configured NuGet source. A lightweight secret scan found code-level password/token identifiers but no committed credentials or private keys. WiX MSI validation passed after correcting the Start Menu shortcut component scope. The distribution build now produces both a self-contained MSI and a self-contained portable zip.

## Artifact Hashes

```text
VRAMVue-Setup-v6.0.0-win-x64.msi     DCF9ABB82A9CE3EF5B9B7D9B95A4D8F9B912DC4CE9DAE953F870F44515507905
VRAMVue-Portable-v6.0.0-win-x64.zip  A931E731E1F6FACFBDCA1DB3A9D8EDCD6CFE561B2D0B1FF5787D97AED74B663A
```

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
- Portable zip build: passed.

## Hardening Change

Freshly created listener certificates are now persisted as non-exportable current-user keys. Existing certificates are not rewritten automatically; deleting the old `VRAM Vue Local Telemetry` certificate from the current-user certificate store allows the app to create a new non-exportable one.

## Residual Risks

- First certificate pinning is trust-on-first-use. Use trusted networks and verify host/IP identity before entering credentials.
- Basic auth credentials are protected by TLS 1.3 in transit and DPAPI at rest, but should still be unique and strong.
- Authenticated remote clients can kill processes and control associated Windows services. Do not expose the listener port to untrusted networks.

## Maximus Runtime Diagnosis

On `Maximus` (`192.168.1.2`), a copied framework-dependent developer build at `C:\net8.0-windows\VramVue.exe` failed before opening. The Application log reported `.NET Runtime` error 1023 against the target app, and inspection showed:

- `Microsoft.WindowsDesktop.App 8.x` was not installed.
- `C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.14\Microsoft.NETCore.App.runtimeconfig.json` contained NUL bytes and was not valid JSON.

The MSI and portable zip avoid this class of failure because they are self-contained win-x64 distribution builds.
