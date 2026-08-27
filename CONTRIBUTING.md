# Contributing

Thank you for helping improve Codex Home Mover. This is a recovery-sensitive Windows utility, so changes should stay small and verifiable.

## Development requirements

- Windows 10/11
- .NET Framework 4.8 compiler or Developer Pack
- PowerShell 5.1 or newer
- NTFS test volume

## Before submitting a pull request

1. Keep changes focused on one problem.
2. Add or update a disposable-directory test for migration-engine behavior.
3. Run:

   ```powershell
   .\test.ps1
   .\build.ps1 -Configuration Release -OutputName CodexHomeMover.exe
   ```

4. Do not test destructive changes against a real `.codex` directory.
5. Do not commit logs, PDB files, test artifacts, account data, real user paths, or screenshots with machine details.
6. Explain rollback behavior and failure-state safety in the pull request.

## Safety invariants

A change must preserve these rules:

- never remove or replace the source before a complete copy and the selected verification finish;
- reject unsafe roots, reparse-point substitutions, untrusted migration records and unstable target disks;
- use explicit, validated paths for every move or delete;
- keep cancellation at safe checkpoints;
- make final switching reversible until the user explicitly deletes the safety backup;
- never suggest disabling Windows security protections.

## UI and assets

Keep UI text understandable to non-technical users. New images, icons and fonts must have clear redistribution rights. The existing mascot is governed separately by `ASSET-LICENSE.md` and is not covered by the code's MIT License.
