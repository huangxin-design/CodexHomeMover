# Changelog

All notable changes to this project are documented here.

## Unreleased

### Changed

- Adopted the public-facing Chinese name “Codex 搬家小鱼” while retaining “Codex Home Mover” as the English and repository name.

## [0.1.0-beta.1] - 2026-08-26

First public beta candidate.

### Added

- Dark Fluent-style resizable interface with 80%–200% display scaling.
- Automatic `.codex` discovery, fixed-NTFS target recommendation and disk-usage summary.
- Copy-first migration with progress, cancellation, SHA-256 verification and SQLite integrity checks.
- NTFS Junction switching, automatic rollback, migration back to C drive and confirmed safety-backup cleanup.
- Failed-copy resume support with extra-file quarantine.
- Animated mascot progress and a dedicated migration-success dialog.
- Local sandbox tests for long paths, Junctions, ACL handling, read-only files, cancellation, resume and rollback.
- Reproducible release packaging with a strict file allow-list and SHA-256 checksums.

### Fixed

- Long Windows paths that previously failed around 260 characters.
- Atomic replacement of read-only destination files.
- Source-directory switching when inherited permissions or background locks were involved.
- UI crowding, clipped text, fixed-size windows and blurry mascot rendering.

### Security

- Restricted native DLL loading to Windows system directories.
- Removed shell-based Junction creation and hardened destructive operations against tampered records and reparse-point redirection.
- Added local log rotation, privacy guidance and private vulnerability reporting instructions.

### Known limitations

- The Windows executable is not Authenticode-signed and may trigger SmartScreen.
- Only local fixed NTFS targets are supported.
- The original mascot and icon use a separate, restricted asset license; public forks must follow `ASSET-LICENSE.md`.
