# VRAM Op

VRAM Op is a Windows WinForms dashboard for watching CPU, RAM, GPU, and VRAM pressure across multiple PCs. Each copy can collect local telemetry, expose it over a TLS 1.3 HTTPS endpoint with basic auth, and monitor other hosts without opening Remote Desktop.

The UI is dark, DPI-aware, and uses the tray/taskbar icon generated at startup.

Author/contact: Martin / `martin0641@gmail.com`

## Build

```powershell
dotnet build C:\git\vram-op\VramOp.csproj -c Release
```

The built executable is written to:

```text
C:\git\vram-op\bin\Release\net8.0-windows\VramOp.exe
```

## Setup

1. Run `VramOp.exe` on each computer you want to monitor.
2. Open **Settings**.
3. Enable the local telemetry listener, choose a port, and set a username/password.
4. On your dashboard computer, add each remote host with the same host/IP, port, username, and password.
5. Allow the listener port through Windows Firewall if another computer cannot connect.

The update interval accepts values from `250 ms` through `9999 ms`.

## Security

- The listener uses an in-process HTTPS server and requires TLS 1.3.
- Each machine creates and reuses a local self-signed certificate stored in the current user's Windows certificate store.
- Remote clients pin the server certificate SHA-256 hash after the first successful connection. If the certificate changes later, the connection is rejected until the pin is cleared in Settings.
- Basic auth credentials are protected at rest with Windows user-scoped DPAPI in `%APPDATA%\VramOp\settings.json`.

## Notes

- `Local VRAM` is the primary per-process column for freeing discrete GPU VRAM for an LLM model.
- The summary uses `GPU Adapter Memory(*)\Dedicated Usage` for the adapter-level VRAM total when Windows exposes it.
- The per-process `Dedicated ctr` value is kept for diagnostics, but Windows can over-report it badly for some processes. Treat it as advisory, especially for `dwm.exe` and `csrss.exe`.
- The app uses Windows performance counters, so it is not tied to NVIDIA, AMD, or Intel command line tools.
- Some elevated or protected Windows processes are shown but not killable from the app.
- The dashboard shows the top 10 GPU-memory processes for the selected host and can send a kill request to that host.
- Closing or minimizing the window hides it to the tray. Use the tray icon menu to show or exit.

## License

MIT. See `LICENSE`.
