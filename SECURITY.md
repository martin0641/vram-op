# Security Policy

## Supported Version

The currently supported release line is VRAM Vue `6.x`.

## Reporting a Vulnerability

Email `martin0641@gmail.com` with:

- A short description of the issue.
- Steps to reproduce.
- Affected version or commit.
- Whether the listener, remote client, installer, or process/service controls are involved.

Please avoid posting working exploits publicly before there is time to investigate and ship a fix.

## Security Design Notes

- Remote telemetry uses HTTPS with TLS 1.3 only.
- Remote hosts use local self-signed certificates and client-side SHA-256 certificate pinning after first connection.
- Credentials are stored with Windows current-user DPAPI.
- Task-kill and service-control APIs require authentication, but should still only be exposed on trusted networks.
- First connection to a host is trust-on-first-use. For higher assurance, verify the host/IP and certificate fingerprint before entering credentials.
