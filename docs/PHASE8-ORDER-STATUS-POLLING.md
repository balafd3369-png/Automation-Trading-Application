# Phase 8 — Order Status Polling

## Problem (Phase 7)

Manual live orders could remain `SUBMITTED` without terminal broker confirmation.

## Implementation

1. **Immediate settlement** — `BrokerLiveOrderSettlement.WaitForTerminalAsync` after manual live place.
2. **Background worker** — `OrderStatusPollingHostedService` refreshes non-terminal manual LIVE and strategy `live_orders` from broker order book.
3. **Poll log** — `TradingSafetyStore.RecordPoll` → `system_order_status_polls`.

## Configuration

`TradingExecutionSafety` section (`TradingExecutionSafetyOptions`):

- Polling interval, max concurrency, stuck thresholds (see `appsettings.json`).

## Rules

- Non-terminal states polled; terminal orders stop unless verification policy says otherwise.
- Polling errors do not mark orders rejected or filled without broker evidence.
- Stuck / polling failures feed `TradingSafetyIncidentEvaluator` (`StuckSubmittedOrder`, polling failure categories).

## Restart

- Worker resumes from DB state; no resubmission on restart.