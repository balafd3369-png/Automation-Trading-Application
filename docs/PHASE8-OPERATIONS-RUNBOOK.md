# Phase 8 Operations Runbook (summary)

## Emergency gate halt

1. Admin: `POST /api/admin/trading-safety/emergency-gate/halt` with confirmation `I AUTHORIZE EMERGENCY GATE CHANGE`.
2. New live **entry** orders blocked; cancels and broker status reads continue.
3. Clear only after investigation: `POST .../emergency-gate/clear`.

## Circuit breaker open

1. View: `GET /api/admin/trading-safety/summary`.
2. Reset after broker restored: `POST /api/admin/trading-safety/circuit-breaker/reset` (same confirmation phrase).
3. Do **not** resubmit uncertain orders — reconcile first.

## Unknown / stuck orders

1. Monitoring dashboard + incidents `OrderStateUnknown` / `StuckSubmittedOrder`.
2. Query broker book via operations APIs.
3. Manual resolution: `POST /api/admin/trading-safety/orders/{id}/resolve` with evidence (manual LIVE orders only).

## Deploy

1. Halt gate or disarm live before deploy.
2. On restart, gate state restores from DB when `PersistEmergencyGateState` is true.
3. Startup order audit (Phase 7) runs before polling worker resumes.