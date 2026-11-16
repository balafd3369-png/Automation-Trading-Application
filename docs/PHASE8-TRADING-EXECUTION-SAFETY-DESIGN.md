# Phase 8 — Trading Execution Safety Design

## Call graph (live placement)

```
Manual: ManualTradingController.PlaceOrder
  → ManualTradingService.PlaceLiveOrderAsync
    → LiveTradingExecutionSafetyService.AssertPolicy(NewPlacement)
    → BrokerConnectionManager.Orders.PlaceOrderAsync
      → CircuitBreakerBrokerOrderClient → MStockOrderClient
    → BrokerLiveOrderSettlement.WaitForTerminalAsync
    → TradeHistoryStore.UpsertManualOrder

Strategy: LiveAgentController / zone auto
  → LiveExecutionCoordinator.ConfirmAsync
    → LiveTradingExecutionSafetyService.AssertPolicy(NewPlacement)
    → BrokerConnectionManager.Orders.PlaceOrderAsync (decorated)
    → WaitForTerminalAsync → TradeHistoryStore.UpsertLiveOrder

Risk reduction (EXIT/COMPENSATION tags): bypass circuit trip for placement; emergency gate does not block.
Cancels / GetOrders: never blocked by circuit breaker trip (heartbeat only).
```

## Phase 7 gaps addressed

| Gap | Phase 8 change |
|-----|----------------|
| Manual SUBMITTED without poll | `BrokerLiveOrderSettlement` after place; `OrderStatusPollingHostedService` |
| Gate/breaker unwired | DI in `Program.cs`; policy service; decorator on `IBrokerOrderClient` |
| Reconciliation only alerts | Unchanged — **no auto corrective orders**; incidents via `TradingSafetyIncidentEvaluator` |

## Safety guarantees

- **At-most-one logical submission** per idempotency key (manual order id).
- **No automatic resubmit** on UNKNOWN/TIMEOUT.
- **Emergency gate** blocks new placement & modification; **not** cancellation or tagged EXIT/COMP wrappers.
- **Circuit breaker** blocks new/modify placement when open; read/status/cancel allowed.
- **Fail-safe gate load** optional via `FailSafeWhenGateUnavailable`.

## Broker limitations

- m.Stock does not guarantee exactly-once execution; idempotency is **application-level** (client order id + DB primary key).
- Timeout on place does not imply rejection — status inquiry required.

See also: `PHASE8-OPERATIONS-RUNBOOK.md` (summary in Phase 8 report).