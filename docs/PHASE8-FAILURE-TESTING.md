# Phase 8 — Failure Testing

## Automated tests (repository)

- `TradingSafetyPhase8Tests` — emergency gate blocks placement, circuit breaker blocks placement, manual idempotency, UNKNOWN path does not double-submit.
- `LiveExecutionCoordinatorTests` — updated with `LiveTradingExecutionSafetyService` and heartbeat for breaker session checks.
- Full suite: `dotnet test tests/TradeWithBeast.Tests/TradeWithBeast.Tests.csproj -c Release`.

## Section 12 matrix (status)

| Area | Coverage |
|------|----------|
| Emergency gate | Partial — core block tests |
| Circuit breaker | Partial — open/trip tests |
| Polling | Integration via hosted service; limited unit tests |
| Unknown orders | Manual/strategy failure paths covered in coordinator + phase8 tests |
| Idempotency | Manual idempotency test |
| Reconciliation | Phase 7 tests; no auto-fix by design |
| Regression Phase 1–7 | Full `dotnet test` |

## Not performed in CI

- Real-money broker failure injection.
- Multi-worker polling contention soak tests.

Use broker simulators / isolated SQLite for new failure-injection tests before production validation.