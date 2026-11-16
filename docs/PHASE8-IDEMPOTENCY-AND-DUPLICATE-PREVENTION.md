# Phase 8 — Idempotency and Duplicate Prevention

## Manual live

- `ManualTradingPlaceOrderRequest.IdempotencyKey` defaults to generated order id.
- `TradeHistoryStore` primary key on order id; repeat request with same key returns existing non-terminal order **without** second broker call.

## Strategy live

- Client order tags tied to trade/order records in `TradeHistoryStore` (existing journey model).

## Guarantees

| Layer | Guarantee |
|-------|-----------|
| API / service | Reject or return existing logical order for duplicate key |
| Database | Unique id per logical order |
| Broker | Client order id sent where supported; **not** exactly-once at broker unless broker documents it |

## Duplicates

- Duplicate attempts should be logged for investigation; incident category `DuplicateOrderAttempt` defined in evaluator (minimal wiring).

## Restart

- `OrderStartupSafetyScanner` (Phase 7) audits outstanding orders; Phase 8 polling resumes status checks — **no blind replay** of submit commands.