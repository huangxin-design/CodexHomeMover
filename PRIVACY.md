# Privacy

Codex Home Mover runs locally and has no telemetry, analytics, updater, advertising, or network upload feature.

## Data the program accesses

The program reads the selected `.codex` directory so it can calculate size, copy files, verify hashes, check SQLite databases, and preserve directory structure and permissions. It does not intentionally inspect file contents beyond those integrity checks.

## Local records and logs

The program stores a migration record and logs under:

```text
%LOCALAPPDATA%\CodexHomeMover
```

Logs may contain:

- the Windows user name as part of an absolute path;
- local directory and file names;
- drive letters, file counts and sizes;
- process names and process IDs;
- exception messages and diagnostic stack traces.

Logs rotate at 5 MB and retain at most `latest.log` and `previous.log`. They are not uploaded automatically.

## Sharing diagnostic information

Before posting an Issue, copy only the few relevant lines and replace user names, project names, task identifiers and private paths with placeholders. Never upload `.codex`, `auth.json`, database files, full logs, or account/session data.
