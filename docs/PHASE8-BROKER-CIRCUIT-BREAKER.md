# Phase 8 — Broker Circuit Breaker

## Registration

- `BrokerCircuitBreaker` singleton in `Program.cs` (injects `Lazy<BrokerConnectionManager>` to avoid a DI cycle with `CircuitBreakerBrokerOrderClient`).
- `CircuitBreakerBrokerOrderClient` decorates `MStockOrderClient` as `IBrokerOrderClient`.

## States

- **Closed** — normal broker calls.
- **Open** — consecutive failures exceeded threshold; new placements and modifications blocked.
- **Half-open** — controlled via manual reset / recovery policy after broker health restored.

## Request classification

- Placement/modify failures increment consecutive failure count and may trip.
- Authentication vs transient errors distinguished in breaker logic (see `BrokerCircuitBreaker`).
- Rate limits handled without unbounded retry loops.

## Exceptions when open

- `GetOrdersAsync`, cancellations, and status-style reads are **not** blocked (order recovery).
- EXIT/COMP-tagged placements bypass trip block (risk reduction only).

## Ambiguous submission

- Timeout does **not** imply rejection; order may be UNKNOWN/TIMEOUT locally.
- No automatic resubmit from breaker path.

## Admin

- `POST /api/admin/trading-safety/circuit-breaker/reset` with confirmation phrase.
- Snapshot exposed on `GET /api/admin/trading-safety/summary` and trading-safety dashboard.

## Incidents

- `TradingSafetyIncidentEvaluator` raises `CircuitBreakerOpen` via existing Phase 3 pipeline.