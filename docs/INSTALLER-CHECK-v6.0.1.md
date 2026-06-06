# Installer Check - v6.0.1

Date: 2026-06-05
Version: 6.0.1
Installer: `dist\VRAMVue-Setup-v6.0.1-win-x64.msi`

## Summary

The v6.0.1 MSI adds the standard WiX install-directory UI, stores the chosen install folder for future upgrades, reports `InstallLocation` in Add/Remove Programs, and removes installer-owned files, shortcut entries, registry entries, and empty install directories during uninstall.

Per-user settings remain in `%APPDATA%\VramOp` so saved hosts and preferences survive upgrades and reinstalls.

## Commands Run

```powershell
.\scripts\Build-Msi.ps1 -Version 6.0.1
wix msi validate .\dist\VRAMVue-Setup-v6.0.1-win-x64.msi
dotnet build .\VramOp.csproj -c Debug --no-restore
```

## Manual MSI Smoke Test

The local installed v6.0.0 package was upgraded to v6.0.1:

- Result: success.
- Add/Remove Programs version after upgrade: `6.0.1`.
- Add/Remove Programs install location after upgrade: `C:\Program Files\VRAM Vue\`.

The package was then uninstalled:

- Result: success.
- `C:\Program Files\VRAM Vue` removed.
- `HKLM\Software\VRAM Vue` removed.
- Add/Remove Programs entry removed.

Custom folder install was tested with:

```powershell
msiexec /i .\dist\VRAMVue-Setup-v6.0.1-win-x64.msi INSTALLFOLDER="C:\Temp\VRAMVueMsiSmoke\" /qn /norestart
```

- Result: success.
- Add/Remove Programs install location: `C:\Temp\VRAMVueMsiSmoke\`.
- `VramVue.exe` installed to the custom folder.

Custom folder upgrade was tested by upgrading that install to a throwaway v6.0.2 build without passing `INSTALLFOLDER`:

- Result: success.
- Add/Remove Programs version after upgrade: `6.0.2`.
- Add/Remove Programs install location remained `C:\Temp\VRAMVueMsiSmoke\`.

The throwaway v6.0.2 install was then uninstalled:

- Result: success.
- `C:\Temp\VRAMVueMsiSmoke` removed.
- `HKLM\Software\VRAM Vue` removed.
- Add/Remove Programs entry removed.

Final state after testing:

- v6.0.1 reinstalled to `C:\Program Files\VRAM Vue\`.
- `VramVue.exe` relaunched successfully from the installed location.
