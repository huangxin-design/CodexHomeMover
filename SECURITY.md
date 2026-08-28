# Security policy

## Supported version

Security fixes are provided for the latest GitHub pre-release or release only. Older ZIP files and executables are not supported.

## Reporting a vulnerability

Please do not open a public Issue for a vulnerability that could affect files, permissions, migration records, Junction targets, or administrator privileges.

Use the repository's **Security → Report a vulnerability** form (GitHub Private Vulnerability Reporting). Include:

- the exact version and SHA-256 of the ZIP;
- the affected operation and Windows version;
- minimal reproduction steps using disposable test directories;
- a redacted error message or log excerpt, if needed.

Do not attach a real `.codex` directory, `auth.json`, SQLite databases, account data, full logs, or screenshots containing private paths. The project will acknowledge a valid report when it is reviewed and coordinate a fix before public disclosure.

## Security model

Codex 搬家小鱼 (Codex Home Mover) requires administrator privileges only because final directory switching, NTFS Junctions, and access-control preservation need them. The application is offline and does not intentionally transmit files, logs, or telemetry.

The current community build is not Authenticode-signed. Verify downloads using the SHA-256 file published with the GitHub Release. Never disable Microsoft Defender or weaken Windows security settings to run the tool.
