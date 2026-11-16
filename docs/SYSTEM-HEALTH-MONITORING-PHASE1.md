# System health monitoring — Phase 1

Phase 1 adds in-process monitoring only (no automatic live-trading blocks, no Angular dashboard).

## Configuration (`SystemMonitoring`)

| Setting | Default | Purpose |
|---------|---------|---------|
| `Enabled` | `true` | Master switch for scheduled probes |
| `WorkerHeartbeatCheckSeconds` | `15` | Monitor self-heartbeat cadence |
| `WorkerHeartbeatStaleSeconds` | `45` | Worker considered stopped when exceeded |
| `DatabaseCheckSeconds` | `30` | SQLite read/write probe interval |
| `ComprehensiveCheckSeconds` | `600` | Full report interval (10 minutes) |
| `PublicHealthIncludeDatabaseProbe` | `true` | Optional DB probe on `GET /api/health` |

## APIs (admin)

- `GET /api/admin/system-health/live` — current snapshot
- `POST /api/admin/system-health/probe/database` — on-demand DB probe
- `POST /api/admin/system-health/reports/comprehensive` — run and persist full report
- `GET /api/admin/system-health/reports` — historical comprehensive reports

## Persistence

SQLite tables `system_health_reports` and `system_health_probes` (same database file as trade history).

## DigitalOcean

Use an **external** HTTP check against `GET /api/health` in addition to this feature. Set `SystemMonitoring__ComprehensiveCheckSeconds` and related keys via environment variables (`__` nesting).