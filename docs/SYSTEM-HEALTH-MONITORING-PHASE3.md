# System health monitoring — Phase 3

Phase 3 adds **incident management** and an internal **alert recording engine**. It consumes Phase 1/2 health snapshots only. It does **not** block trading, send email/Telegram, or change live execution.

## Architecture

```
SystemIncidentEvaluationHostedService (timer)
  → SystemIncidentService.EvaluateAsync
      → SystemIncidentEvaluator (reads SystemHealthService)
      → SystemIncidentStore (SQLite dedupe + lifecycle)
      → SystemIncidentAlertService → IIncidentAlertDispatcher (database-backed)

SystemIncidentController (admin APIs)
```

## SQLite schema

Tables (same database file as trade history / health reports):

- `system_incidents` — active fingerprint unique partial index (`Detected`, `Acknowledged`, `Investigating`)
- `system_incident_events` — audit trail
- `system_incident_notes` — admin notes
- `system_incident_alerts` — alert records (PENDING → DISPATCHED = persisted only)

Phase 1 `system_health_reports` unchanged.

## Lifecycle

`Detected` → `Acknowledged` → `Investigating` → `Resolved` → `Closed`

- **Auto-resolve** (config): when health recovers, status moves to `Resolved` via `RECOVERED` event — **not** `Closed`.
- **Close** remains an explicit admin action.

## Deduplication

Fingerprint: `{component}|{category}|{componentIdentifier}` (lowercase).

Active incidents with the same fingerprint are updated (occurrence count++, last error/time).

## Alerts

- `IIncidentAlertDispatcher` implemented by `DatabaseIncidentAlertDispatcher` (no external delivery).
- Cooldown / minimum severity via `SystemMonitoring` options.
- Status `DISPATCHED` means stored for operators — not sent to email/Telegram.

## Configuration

See `SystemMonitoring` in `appsettings.json`: `IncidentManagementEnabled`, `IncidentEvaluationIntervalSeconds`, `IncidentAutoResolveEnabled`, `AlertMinimumSeverity`, `AlertCooldownSeconds`, etc.

## Admin APIs

Under `/api/admin/incidents` and `/api/admin/alerts` (admin auth required). See controller for full list.

## Testing

```bash
dotnet test backend/tests/TradeWithBeast.Tests/TradeWithBeast.Tests.csproj -c Release --filter "SystemIncident"
```

## Limitations

- Single application instance dedupe (no distributed lock).
- Alert dispatch is persistence-only until Phase 4+ notification providers.
- Worker recovery emits healthy signals per worker incident category for correct fingerprint resolution.