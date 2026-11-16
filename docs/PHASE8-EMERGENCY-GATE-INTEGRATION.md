# Phase 8 — Emergency Gate Integration

See `PHASE8-TRADING-EXECUTION-SAFETY-DESIGN.md` for the live placement call graph.

## Components

- `LiveTradingEmergencyGate` — in-memory halt flag (risk controls companion).
- `LiveTradingExecutionSafetyService` — policy enforcement and persisted state via `TradingSafetyStore`.
- `TradingSafetyController` — admin halt/clear (`/api/admin/trading-safety/emergency-gate/*`).
- `OperationsSuiteService` — `HALT_LIVE_ORDERS` emergency action (same halt, operations confirmation phrase).

## Policies

| Operation | When halted |
|-----------|-------------|
| New placement | Blocked |
| Modification | Blocked |
| Cancellation | Allowed |
| Status inquiry | Allowed |
| Risk reduction (EXIT/COMP tags) | Placement allowed (documented exception) |

## Persistence and audit

- State table: `system_emergency_gate_state`.
- Audit table: `system_emergency_gate_audit`.
- Admin confirmation phrase: `I AUTHORIZE EMERGENCY GATE CHANGE` (trading-safety APIs).

## Fail-safe

- If gate state cannot be loaded and `FailSafeWhenGateUnavailable` is enabled, new placement is denied.
- Gate failures must not silently allow live placement.

## Dashboard

- Angular: `#/admin/monitoring/trading-safety` — gate state, halt/clear, audit list.