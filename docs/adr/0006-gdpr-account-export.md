# ADR-0006: GDPR account data export

**Date:** 2026-06-27  
**Status:** Accepted  
**Deciders:** OwnPlanner maintainers

---

## Context

GDPR Art. 15 (right of access) and Art. 20 (data portability) require that a user can obtain a
complete, self-contained, machine-readable copy of their personal data on demand, scoped strictly to
themselves. OwnPlanner's data model (see `docs/database-schema.md`) already isolates each user's
planning data in its own SQLite file, `ownplanner-user-{userId}.db` (`AppDbContext`:
`PlanningContexts`, `Goals`, `TaskLists`/`TaskItems`, `NoteLists`/`NoteItems`). The central
`ownplanner-auth.db` holds credentials, tokens, and per-day AI usage counters across all users.

Two facts narrowed the design:

- The per-user database file **is** the user's planning data, already strictly single-user. Handing
  back a copy of that file satisfies "complete, self-contained, machine-readable, no other users'
  data" without a bespoke serialization format.
- The only persisted "AI history" is `UserDailyUsage` (request/token counts) in the auth DB; chat
  conversation history is in-memory only and never persisted. The auth DB also holds the password
  hash. Neither belongs in a portability export of *planning* data.

## Decision

A self-service, synchronous export delivered as a ZIP download.

### 1. Export shape

The export is a ZIP containing:
- `ownplanner-data.db` — a consistent snapshot of the user's planner SQLite database.
- `README.txt` — a constant, human-readable note explaining the file is a standard SQLite 3
  database, listing the tables, and how to open it (`sqlite3`, DB Browser for SQLite). It also notes
  that credentials and AI usage statistics are intentionally excluded.

The auth database is **not** included.

### 2. Consistent snapshot via `VACUUM INTO`

`AccountExportService` (`OwnPlanner.Infrastructure/Account/AccountExportService.cs`, implementing
`IAccountExportService` in `OwnPlanner.Application/Account/`) resolves an `AppDbContext` from
`IPlannerDbContextFactory` and runs SQLite `VACUUM INTO '<temp path>'`. This produces a clean,
transactionally consistent copy (folding in any WAL contents) reflecting the data at request time,
without locking or copying the live file by path. The snapshot is written under a unique GUID temp
directory (SQLite refuses to overwrite an existing target), zipped with `System.IO.Compression`, and
the intermediate copy is deleted. The result is a temp `.zip` described by the `AccountExport` record
(path, suggested filename `ownplanner-export-{yyyyMMdd}.zip`, content type).

### 3. Endpoint and delivery

`GET /api/account/export` on `AccountController` (`[Authorize]`) resolves the authenticated user from
the `NameIdentifier` claim (the per-user DB is resolved automatically by the factory). Because the
per-user database is created/migrated lazily on first tool use, the endpoint first calls
`IPerUserAppInitializationService.EnsureInitializedAsync` (extracted from the existing
`PerUserAppInitializationService`) so a user who exports before ever chatting gets a migrated, seeded
database rather than an empty, schemaless file. It then returns the archive as a `FileStreamResult`.
The stream is opened with `FileOptions.DeleteOnClose`, so the temp archive is removed once the
response has been sent. Delivery is synchronous: the download itself is how the user is "informed when
ready", which is appropriate at the current per-user data scale.

### 4. Temp file lifecycle

Each export creates two temp artifacts under the system temp dir, both prefixed
`AccountExportService.TempEntryPrefix` (`ownplanner-export-`): a working directory holding the
`VACUUM INTO` snapshot, and the final ZIP. The working directory is deleted in a `finally` right
after packaging. The ZIP is normally removed by `FileOptions.DeleteOnClose` when the response stream
closes; if opening that stream fails, the controller deletes it explicitly. Because a process crash,
an aborted request, or an OS-level delete failure can still orphan an artifact, a hosted
`ExportTempFileCleanupService` sweeps the temp dir every 15 minutes and removes any matching entry
idle longer than 30 minutes — comfortably above any in-flight export, so a live download is never
reaped. There is no per-file retry; the periodic sweep is the backstop.

### 5. Legal retention

None. OwnPlanner keeps no billing/accounting records, so there is no retained-data exception to the
export (or to erasure — see [ADR-0007: GDPR account deletion](0007-gdpr-account-deletion.md)).

## Consequences

### Positive

- No serialization layer to build or keep in sync with the schema; the export is exactly the data.
- `VACUUM INTO` guarantees a consistent point-in-time snapshot even with an active WAL.
- Strict single-user scoping is inherent — the file only ever contained one user's data.
- `DeleteOnClose` handles the normal case, and a periodic sweep backstops crash/abort orphans, so
  export artifacts don't accumulate on disk.

### Negative / Trade-offs

- The format is SQLite rather than a more universally diff-able format (JSON/CSV). Acceptable: SQLite
  is an open, widely-readable standard, and the README points at common tools. Revisit if users need
  a text format for direct portability into a specific competing product.
- Synchronous generation holds the request open while snapshotting and zipping. Acceptable at current
  scale; revisit (async job + notification) if per-user databases grow large enough to make this slow.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Account/IAccountExportService.cs` | Export service contract |
| `OwnPlanner.Application/Account/AccountExport.cs` | Result DTO (temp path, filename, content type) |
| `OwnPlanner.Infrastructure/Account/AccountExportService.cs` | `VACUUM INTO` snapshot + ZIP packaging + README; `TempEntryPrefix` |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Controllers/AccountController.cs` | `GET /api/account/export` endpoint (+ orphan cleanup on stream-open failure) |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/ExportTempFileCleanupService.cs` | Hosted sweep reaping stale export temp artifacts |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Program.cs` | DI registration of `IAccountExportService` + cleanup hosted service |
| `OwnPlanner.Web/ownplanner.web.client/src/services/api.ts` | `exportAccountData()` blob download |
| `OwnPlanner.Web/ownplanner.web.client/src/pages/SettingsPage.tsx` | "Your data" export section |
