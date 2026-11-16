# Phase 7 — Production Readiness & Trading Safety Audit

**Audit date:** 2026-03-16  
**Scope:** Full stack (ASP.NET Core 8 + SQLite + Angular 19) after Phases 1–6 monitoring/reliability and existing live trading paths.

**Overall readiness:** **Conditionally ready for controlled live operation** — monitoring, incidents, notifications, coordination, backups, and read-only order audits are in place. **Not** fully production-hardened for unattended multi-instance live trading without addressing **Critical** and **High** findings below.

---

## Executive summary

| Area | Status | Notes |
|------|--------|--------|
| Health & incidents (Phases 1–3) | **Strong** | Broker/market/worker/DB probes; deduplicated incidents |
| Notifications (Phase 4) | **Strong** | Email/Telegram, leases, retries (at-least-once) |
| Dashboard (Phase 5) | **Strong** | Admin monitoring UI |
| Coordination (Phase 6) | **Good (single/shared SQLite)** | Locks, leader election (opt-in), safe recovery **denies trading workers** |
| Production safety (Phase 7) | **Good** | SQLite backup, startup order audit, reconciliation → incidents |
| Order execution safety | **Gaps remain** | Manual live stops at SUBMITTED; emergency gate / circuit breaker **not wired in DI** |

---

## Component audit (selected)

### Application startup / shutdown

| | |
|--|--|
| **Implementation** | `Program.cs` hosted services; `DeploymentStartupValidator`; Phase 6 `RunStartupRecovery()`; Phase 7 `OrderStartupSafetyScanner` + scheduled backup |
| **Failures** | Abrupt kill mid-order; no host-level drain of live coordinator |
| **Protection** | Startup read-only order audit; notification lease release |
| **Gaps** | No graceful in-flight order completion on shutdown |
| **Severity** | **High** (live) |
| **Remediation** | Document runbook: disarm live before deploy; optional `IHostApplicationLifetime` drain (non-trading first) |
| **Tests** | Phase 6 restart tests; Phase 7 backup + incident tests |

### Broker connectivity (Phase 2 + 7)

| | |
|--|--|
| **Implementation** | `SystemHealthInfrastructureProbes`; incidents via `SystemIncidentEvaluator`; AUTH heuristic when credentials configured but no session |
| **Failures** | Timeout, no session, quote probe failure |
| **Protection** | Consecutive failure threshold; **no automatic order placement on reconnect** |
| **Gaps** | Rate-limit/maintenance not classified separately; WebSocket not instrumented |
| **Severity** | **Medium** |
| **Remediation** | Extend probe metadata; ops runbook for session renewal |
| **Tests** | `SystemHealthPhase2Tests` |

### Market data (Phase 2)

| | |
|--|--|
| **Implementation** | Instrument staleness; `UpdatesExpected` respects session calendar |
| **Protection** | Incidents for stale/disconnected; strategies should use fresh quotes (verify per strategy) |
| **Gaps** | No automatic strategy pause on stale data (by design — observational) |
| **Severity** | **Medium** |
| **Remediation** | Confirm `LiveExecutionCoordinator` prechecks before arm |
| **Tests** | Phase 2 stale board test |

### Order lifecycle & reconciliation

| | |
|--|--|
| **Implementation** | `TradeHistoryStore` (`live_orders` with `ux_live_orders_broker`); `LiveExecutionCoordinator` settlement poll; `BrokerReconciliationService` + engine; Phase 7 startup audit |
| **Protection** | Compare-only reconciliation; **no auto corrective orders**; startup audit **never resubmits** |
| **Gaps** | **Critical:** Manual live `PlaceLiveOrderAsync` does not poll terminal state; **High:** `LiveTradingEmergencyGate` / `BrokerCircuitBreaker` exist but **not registered** in `Program.cs`; limited idempotency at broker layer |
| **Severity** | **Critical / High** |
| **Remediation** | Wire safety gate before enabling unattended live; extend manual live with read-only status poll (no resubmit); operator procedures for UNKNOWN |
| **Tests** | `BrokerReconciliationEngineTests`, `LiveExecutionCoordinatorTests`, Phase 7 |

### Database & backup (Phase 7)

| | |
|--|--|
| **Implementation** | `SqliteDatabaseBackupService` (checkpoint + `BackupDatabase`); history in `system_database_backups` |
| **Protection** | Integrity verify; failed backup → incident |
| **Gaps** | Off-machine backup copy is operational (not automated upload) |
| **Severity** | **Medium** |
| **Remediation** | See `PHASE7-DATABASE-DISASTER-RECOVERY.md` |
| **Tests** | `ProductionSafetyPhase7Tests` |

### Trading emergency controls

| | |
|--|--|
| **Implementation** | `OperationsSuiteService` emergency actions; `LiveTradingEnabledAttribute`; config gates |
| **Gaps** | In-memory `LiveTradingEmergencyGate` not integrated into order pipeline |
| **Severity** | **Critical** for capital protection until wired or superseded by ops procedures |
| **Remediation** | Integrate gate in live order path with explicit tests **or** enforce manual disarm + broker cancel SOP |

---

## Prioritized findings

### Critical

1. **Manual live orders may remain SUBMITTED without broker terminal confirmation** — timeout does not imply rejection; no automatic duplicate submission (good), but state may be uncertain until operator or startup audit runs.
2. **`LiveTradingEmergencyGate` / circuit breaker not in production DI** — documented safety types are not enforcing halts on the main `IBrokerOrderClient` path.

### High

3. **No host shutdown coordination for live coordinator** — deploy during live window requires runbook.
4. **Broker reconciliation mismatches** — now raise incidents (Phase 7); still require human resolution (by design).
5. **SQLite single-file** — backup mitigates data loss; cross-host coordination requires shared DB (see Phase 6).

### Medium

6. **At-least-once notifications** (Phase 4 limitation).
7. **Broker auth vs disconnect** — improved via AUTH prefix during market hours; not a full OAuth health API.
8. **Rate limits / WebSocket drops** — partially covered by generic broker API failure incidents.

### Low

9. **Order journey store / correlation API** — implemented but not wired (docs only).
10. **Angular production-safety page** — admin APIs available; extend dashboard as needed.

---

## Phase 7 deliverables (implemented)

- `ProductionSafetyOptions`, `ProductionSafetyHostedService` (scheduled backup + startup order audit)
- `SqliteDatabaseBackupService`, `ProductionSafetyStore`, admin `ProductionSafetyController` (`/api/admin/production-safety/*`)
- `ProductionSafetyIncidentEvaluator` → reconciliation, backup failure, uncertain startup orders
- Broker probe AUTH distinction for session missing with credentials configured
- New incident categories: `OrderReconciliationMismatch`, `OrderStateUnknown`, `OrderReconciliationUnavailable`, `DatabaseBackupFailed`

---

## Verification

| Check | Result |
|--------|--------|
| `dotnet build -c Release` | Success |
| Backend tests | **265 passed** (includes Phase 7 tests) |

---

## Production deployment recommendations

1. Enable `ProductionSafety` and `IncidentNotification` with tested recipients before live.
2. Run manual backup once after deploy; confirm incident pipeline receives failures.
3. **Disarm live** and verify broker book before application updates.
4. Do not enable `LeaderElectionEnabled` unless all instances share one SQLite file.
5. Treat reconciliation and uncertain-order incidents as **stop-and-investigate** — never auto-resubmit.