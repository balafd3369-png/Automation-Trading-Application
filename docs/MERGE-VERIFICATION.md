# Pre-merge verification (instrument scaffold)

Use this checklist before merging instrument / futures scaffold work.  
**No changes** to `PaperDecisionEngine`, `PortfolioRiskManager`, `ZoneStrategyRollout`, `PaperTradingAgent` auto-admit, or option order placement logic.

## Automated regression (run with backend stopped so DLL is not locked)

```bash
cd backend/tests/TradeWithBeast.Tests
dotnet test --filter "FullyQualifiedName~StrategyExecutionRegressionTests"
dotnet test --filter "FullyQualifiedName~InstrumentTypeNormalizerTests"
dotnet test --filter "FullyQualifiedName~LiveExecutionCoordinatorTests"
```

```bash
cd frontend
npm test -- --include=**/instrument-type.util.spec.ts
```

| Test # | Automated coverage |
|--------|-------------------|
| 1 Zone auto live (options) | `Zone_auto_live_option_proposal_still_allowed_when_futures_disabled` |
| 2 Manual live (options) | `Hedge_places_buy_first…`, `Naked_sell_preserves_margin…` (unchanged paths) |
| 5 Future live blocked | `Future_leg_blocked_from_live_when_futures_disabled`, `Future_proposal_blocked_live_but_kind_parses_for_paper` |
| 4 History OPTION default | `InstrumentTypeNormalizerTests`, `formatInstrumentTypeDisplay` (frontend), leg read normalize in `TradeHistoryStore.ReadHistoryLegs` |

## Manual verification

### Test 1 — Zone auto live

Prerequisites: m.Stock session, `LiveTrading:Enabled`, zone auto armed (`GO LIVE ZONE CSV ON REAL MONEY`), indices **NIFTY / BANKNIFTY / FINNIFTY / SENSEX** in rollout **paper_auto** where applicable.

1. Wait for zone signal on an eligible index.
2. Confirm flow: **signal → proposal → broker submit** (no extra confirm).
3. Repeat for each index you rely on in production.

**Expected:** Same as before scaffold; only **OPTION** (or cash) legs hit the broker. Guard runs **after** zone preflight; FUTURE legs are not used by zone CSV live.

### Test 2 — Manual live

1. Arm manual (`I UNDERSTAND LIVE ORDERS USE REAL MONEY`).
2. Accept a catalog **option** proposal → **Confirm** → MIS/NRML.
3. Confirm broker order appears and fills (or fails with broker message, not futures-disabled message).

**Expected:** Option confirm path unchanged.

### Test 3 — Paper auto

1. Start paper agent with `AutoAdmitTrades = true` (default on start).
2. Let a passing signal arrive (zone paper_auto or catalog auto).

**Expected:** Paper fill without chat `CONFIRM`; behavior identical to pre-scaffold.

### Test 4 — History instrument type

1. Open **Trade Details → History** (and strategy command center paper section).
2. Open trades from before scaffold (and new trades).

**Expected:** Leg/instrument display shows **OPTION** (or explicit FUTURE/STOCK), not blank/null/Unknown. API normalizes empty `instrument_type` on read to `OPTION`.

### Test 5 — Forced FUTURE

1. **Paper:** Use a context where Ten AM (or naked future strategy) produces a **FUTURE** leg — paper should still simulate.
2. **Live:** Attempt live confirm on a proposal with **FUTURE** leg while `TradingPlatform:FuturesEnabled` is **false**.

**Expected:** Paper allowed; live fails with  
`Live futures routing is disabled (TradingPlatform:FuturesEnabled=false).`  
and **no** broker `PlaceOrder` calls.

Enable futures only when contract routing is implemented:

```json
"TradingPlatform": { "FuturesEnabled": true }
```

## Future model (scaffold only)

- `InstrumentKind` — OPTION, FUTURE, STOCK, …
- `ProductType` — `IndexFuture`, `StockFuture`, `IndexOption`, … (inferred, not persisted yet)

See `backend/Models/ProductType.cs` and `frontend/src/app/core/instrument-type.util.ts`.
