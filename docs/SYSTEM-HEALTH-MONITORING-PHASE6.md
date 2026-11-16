# Phase 6 — Distributed Reliability & Safe Recovery

## Architecture findings

| Topic | Finding |
|--------|---------|
| Database | SQLite file (`Database:SqlitePath` / default `%AppData%/TradeWithBeast/trade-history.db`) |
| Default deployment | **Single process** per machine; pooling disabled |
| Multi-process | Possible only if **all instances share the same SQLite file path** (same host or shared volume) |
| Cross-machine | **Not safe** with a local SQLite file — requires shared DB + coordination (e.g. PostgreSQL, Redis) |
| Worker heartbeats | In-process `WorkerHeartbeatRegistry` (Phase 1) + persisted `system_worker_registry` (Phase 6) |
| Leader election | Opt-in (`SystemReliability:LeaderElectionEnabled`, default **false** for backward compatibility) |

## Components

- `IDistributedLockService` / `SqliteDistributedLockService` — atomic lock rows with fencing tokens
- `SystemLeaderElectionService` — SQLite leader leases (disabled by default)
- `SystemInstanceRegistryService` — instance heartbeat & drain
- `SystemWorkerRegistryPublisher` — publishes in-process heartbeats to DB
- `SystemRecoveryService` / `SystemRecoveryPolicyService` — **deny-by-default** for trading workers
- `SystemReliabilityCoordinatorHostedService` — instance heartbeat, leader renewal, worker publish, startup recovery
- Notification claims — processing leases on `system_incident_notifications`

## Trading safety

Automatic recovery is **denied** for:

- `PaperTradingAgent`
- `LiveExecutionCoordinator`
- `BrokerReconciliationService`
- `MockMarketService`

`LiveTradingEmergencyGate` is unchanged. No order resubmission or strategy restart.

## Configuration (`SystemReliability`)

See `appsettings.json`. Key flags:

- `LeaderElectionEnabled` — set `true` only when multiple instances share one SQLite DB
- `NotificationProcessingLeaseSeconds` — stuck `Processing` recovery
- `SqliteWalMode` / `SqliteBusyTimeoutMs` — concurrency tuning

## Admin APIs

Under `/api/admin/system/*` — instances, workers, locks, leader, recovery history/policies, manual recovery (admin only).

## Angular dashboard (Phase 5 extension)

Under `#/admin/monitoring/*`:

| Route | Purpose |
|--------|---------|
| `reliability` | Summary cards (instances, workers, locks, recovery rate) |
| `instances` | Instance registry, drain control (with confirm) |
| `coordination-workers` | Persisted worker registry; manual recovery only for non-trading workers |
| `locks` | Active distributed locks |
| `recovery` | Policies + recovery audit history |

APIs: `SystemMonitoringService` methods calling `/api/admin/system/*` (admin auth required).

## Limitations

- Verified for **single-instance** and **shared-file multi-process** on one machine in tests
- **At-least-once** notification delivery remains possible across provider timeout + crash
- Cross-host coordination is **not** claimed without shared infrastructure

## Verification (latest)

- **Backend Release build:** success
- **Backend tests:** **263 passed**, 0 failed
- **Angular `ng build`:** success (existing `app.component.scss` budget warning)