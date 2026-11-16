# Manual live risk — post-restricted-deploy remediation plan

**Scope:** Do **not** set `LiveTrading:Enabled=true` until these items are implemented and verified. Emergency gate and circuit breaker remain mandatory.

## G1 — Daily loss limits on manual live

- **Gap:** `ManualTradingService` uses `ManualOrderCapitalValidator` (broker funds) only; `PortfolioRiskManager` daily loss applies to paper agent, not manual live.
- **Plan:** Reuse agent config `MaxDailyLoss` + realized PnL snapshot before `PlaceLiveOrderAsync`; reject with actionable message; add unit tests.

## G2 — Global maximum open positions (manual live)

- **Gap:** No cap on concurrent manual live open orders/positions.
- **Plan:** Count non-terminal manual live orders + open live trades; enforce `Trading:DefaultMaxOpenPositions` or per-user setting.

## G3 — Daily live order count

- **Gap:** `MaxTradesPerDay` not enforced on manual live path.
- **Plan:** Query `TradeHistoryStore` for today's manual live submissions; block before broker call; mirror `OrderJourneyStages.DailyOrderLimitExceeded`.

## G4 — Phase 8 failure-injection completion

- **Gap:** `PHASE8-FAILURE-TESTING.md` — partial matrix; no full 50-case broker failure suite.
- **Plan:** Add simulators for timeout, duplicate key, concurrent confirm, breaker trip recovery; run in CI on non-OneDrive agents.

## Verification checklist (before live money)

- [ ] Integration tests for G1–G3
- [ ] Signed runbook walkthrough with emergency halt
- [ ] Explicit approval to set `LiveTrading:Enabled=true`