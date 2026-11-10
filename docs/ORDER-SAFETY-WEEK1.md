# Order safety & observability (Week 1 beta)

Closed-beta requirements for manual/live order paths: one correlation id, persisted timeline, broker circuit breaker, diagnostics API, and a tester operations view.

## Correlation IDs

Format: `TWB-ORD-YYYYMMDD-XXXXXX` (see `OrderCorrelation.NewId()`).

Generated at the start of:

- Manual orders — `ManualTradingService.PlaceOrderAsync`
- Strategy live confirms — `LiveExecutionCoordinator.ConfirmAsync`

Returned to the UI on `POST /api/manual-trading/orders` as `correlationId` (success and failure).

## Order journey timeline

Stages (constants in `OrderJourneyStages`):

`Submitted`, `Validated`, `RiskApproved`, `RiskRejected`, `CircuitBreakerBlocked`, `BrokerSent`, `BrokerAccepted`, `BrokerRejected`, `PartiallyFilled`, `Filled`, `Cancelled`, `Failed`

Stored in SQLite (`order_journey`, `order_journey_events`) via `OrderJourneyStore`. Audited in the operations log via `OrderJourneyRecorder`.

## Broker circuit breaker

`BrokerCircuitBreaker` trips when:

- 3 consecutive broker order failures, or
- Broker session invalid during live window, or
- No successful broker heartbeat during live market hours

While tripped: **live manual and strategy orders are blocked** until `POST /api/operations/circuit-breaker/reset`.

Broker API calls run through `CircuitBreakerBrokerOrderClient` (decorator on `IBrokerOrderClient`).

## Diagnostics API

| Endpoint | Purpose |
|----------|---------|
| `GET /api/ops/orders/{correlationId}` or `GET /api/ops/order/{correlationId}` | Journey lookup (alias of `/api/operations/orders/...`) |
| `GET /api/operations/tester-dashboard` | Broker health, breaker, recent rejections/orders, live events |
| `POST /api/operations/circuit-breaker/reset` | Manual breaker reset |

## Tester UI

Operations → **Tester** tab: correlation search, breaker status, recent rejections/orders, live events.

## Launch defaults (5 testers)

Enable: paper + manual trading, audit, emergency stop, risk limits, correlation IDs.

Keep disabled by default: live strategy auto-execution, unattended live, experimental strategies until telemetry is collected.
