# Phase 7 — Database Backup & Disaster Recovery (SQLite)

## Overview

Production database: **`trade-history.db`** (path from `Database:SqlitePath` or default `%AppData%/TradeWithBeast/trade-history.db`).

Phase 7 uses SQLite’s **online backup API** (`SqliteConnection.BackupDatabase`) after `PRAGMA wal_checkpoint(TRUNCATE)` — **do not** copy the `.db` file while writers are active without checkpointing.

## Configuration (`ProductionSafety`)

```json
"ProductionSafety": {
  "Enabled": true,
  "DatabaseBackupEnabled": true,
  "DatabaseBackupIntervalHours": 24,
  "DatabaseBackupRetentionCount": 14,
  "DatabaseBackupDirectory": "",
  "DatabaseBackupVerifyAfterCreate": true,
  "StartupOrderAuditEnabled": true,
  "StartupOrderAuditDelaySeconds": 15,
  "ReconciliationIncidentsEnabled": true
}
```

- Empty `DatabaseBackupDirectory` → `%AppData%/TradeWithBeast/backups/`
- Backups named `trade-history-yyyyMMdd-HHmmss-{id}.db`

## Administrative APIs (admin auth required)

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/admin/production-safety/status` | Summary (last backup, audit, reconciliation counts) |
| GET | `/api/admin/production-safety/backups` | History |
| POST | `/api/admin/production-safety/backups` | Trigger backup now |
| POST | `/api/admin/production-safety/backups/{id}/verify` | `PRAGMA integrity_check` on backup file |
| GET | `/api/admin/production-safety/order-audit/latest` | Latest startup order audit |
| GET | `/api/admin/production-safety/reconciliation` | Current broker reconciliation snapshot |

Backup file paths are **not** exposed as public static files.

## Restore procedure (manual — never auto-overwrite production)

**Use a separate test database first.**

1. **Stop the application** (or ensure no process holds the DB).
2. Copy the chosen backup file to a **new path**, e.g. `trade-history-restore-test.db`.
3. Verify integrity:
   - `POST /api/admin/production-safety/backups/{id}/verify`, or
   - `sqlite3 trade-history-restore-test.db "PRAGMA integrity_check;"`
4. Point a **non-production** instance at the test path via `Database:SqlitePath` and smoke-test admin health + trade history read APIs.
5. For production restore: stop app → rename current DB to `trade-history.db.bak-{timestamp}` → copy verified backup to production path → start app → run startup order audit and broker reconciliation → resolve any incidents.

## Failure handling

- Failed or unverified backups create **`DatabaseBackupFailed`** incidents (High).
- Schedule: `ProductionSafetyHostedService` every `DatabaseBackupIntervalHours` (checks every 5 minutes).

## Tested recovery

`ProductionSafetyPhase7Tests.Sqlite_backup_creates_verified_file` creates a temp DB, runs backup, and verifies integrity on the backup file.

## Limitations

- Backups are **on-box** unless operators copy files to external storage (S3, NAS, etc.).
- WAL mode (Phase 6 `SystemReliability:SqliteWalMode`) is compatible with checkpoint-before-backup.
- Cross-machine HA requires migrating to a shared database — not covered by file backup alone.