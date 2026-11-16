# Phase 8 — Unknown Order Handling

## When UNKNOWN / TIMEOUT is set

- Broker place timeout or lost response.
- Crash after possible broker acceptance.
- Explicit ambiguous result from settlement.

## Behavior

- Preserve client order id and broker id when known.
- **No automatic resubmit** (manual or strategy `ConfirmAsync` fails with explicit message).
- Status remains UNKNOWN/TIMEOUT until broker evidence or admin resolution.

## Investigation

- Use broker order book via polling worker or manual inquiry.
- Reconciliation snapshot (`BrokerReconciliationService`) — **no auto corrective orders**.

## Manual resolution

`POST /api/admin/trading-safety/orders/{id}/resolve` (manual LIVE orders):

- Requires confirmation phrase.
- Sets status `RESOLVED` with operator evidence in message field.
- Audit entry `ORDER_RESOLVE` in gate audit log.

## Incidents

- High-severity unknown / unresolved thresholds via `TradingSafetyIncidentEvaluator`.