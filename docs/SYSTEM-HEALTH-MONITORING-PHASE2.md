# System health monitoring — Phase 2

Phase 2 extends Phase 1 with **read-only** infrastructure monitoring (broker connectivity, market data freshness, SignalR server metrics, and worker failure/progress states). No automatic trading actions, incidents, or alerts.

## Architecture

```
SystemHealthMonitoringHostedService
  ├─ database probes (Phase 1)
  ├─ broker / market / SignalR interval probes
  └─ comprehensive report (Phase 1 + Phase 2 payload)

SystemHealthInfrastructureProbes  ←→  BrokerConnectionManager / IBrokerMarketClient
                                    ←→  ISystemHealthBoardProbeSource (MockMarketService)
                                    ←→  SignalRServerHealthTracker (MarketChartHub + chart push)

WorkerHeartbeatRegistry + WorkerHealthEvaluator  ←→  hosted workers (pulse only)
SystemHealthStore (unchanged tables, extended JSON payload)
SystemHealthController (Phase 1 routes + Phase 2 routes)
```

## Health states (`ComponentHealthState`)

| State | Meaning |
|--------|---------|
| Healthy | Component operating normally |
| Degraded | Errors or partial failure |
| Stalled | Worker alive but no meaningful progress within threshold |
| Unresponsive | Heartbeat exceeded timeout |
| Stopped | Worker reported stopped |
| Unknown | Disabled monitoring or insufficient data |

Worker rules respect market hours via `NseCashSession` (no progress penalty when updates are not expected).

## Configuration (`SystemMonitoring`)

See `appsettings.json` and `SystemMonitoringOptions`. All sections can be disabled independently without breaking startup.

Per-worker overrides: `WorkerOverrides:{WorkerName}:HeartbeatTimeoutSeconds`, `ProgressTimeoutSeconds`.

Environment variables use `__` nesting (e.g. `SystemMonitoring__BrokerMonitoringEnabled=false`).

## Admin APIs

Phase 1 (unchanged):

- `GET /api/admin/system-health/live`
- `POST /api/admin/system-health/probe/database`
- `POST /api/admin/system-health/reports/comprehensive`
- `GET /api/admin/system-health/reports`
- `GET /api/admin/system-health/reports/{id}`

Phase 2:

- `GET /api/admin/system-health/brokers`
- `GET /api/admin/system-health/market-data`
- `GET /api/admin/system-health/signalr`
- `GET /api/admin/system-health/workers`
- `GET /api/admin/system-health/components`
- `GET /api/admin/system-health/components/{component}`
- `POST /api/admin/system-health/probe/broker`
- `POST /api/admin/system-health/probe/market-data`
- `POST /api/admin/system-health/probe/signalr`

## Failure detection (summary)

**Broker:** Session flag + optional read-only NIFTY quote probe when session present. Never places orders. Consecutive probe failures increment toward `BrokerFailureThreshold`.

**Market data:** Board snapshot ages vs `MarketDataStaleThresholdSeconds` only when intraday session open and admin broker connected. Market closed / holiday → not classified as stale.

**SignalR:** Server connection counts and hub broadcast activity only (not end-to-end client chart verification).

**Workers:** Heartbeat timeout vs progress timeout with worker-specific progress expectations.

## Known limitations

- Multi-instance execution ownership not tracked (Phase 6+).
- Broker probe uses a single lightweight quote when connected (subject to m.Stock API limits).
- SignalR health is **server-side**; browser disconnects without a hub connection are invisible here.
- Per-user broker sessions are not modeled (platform uses shared admin broker feed).

## Testing

```bash
dotnet test backend/tests/TradeWithBeast.Tests/TradeWithBeast.Tests.csproj -c Release
dotnet test --filter "SystemHealth"
```

Tests use `IBrokerMarketClient` fakes; no live broker or order placement.