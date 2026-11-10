# Auto Live Risk — Verification Evidence

Run (alternate output path avoids DLL lock when API is running):

```powershell
$out = Join-Path $env:TEMP "twb-test-$(Get-Random)"
dotnet test backend\tests\TradeWithBeast.Tests\TradeWithBeast.Tests.csproj `
  --filter "FullyQualifiedName~LiveExecution" `
  -p:OutputPath="$out\" -p:IntermediateOutputPath="$out\obj\"
```

## Summary matrix

| Scenario | Result | Test / code path |
|----------|--------|------------------|
| Daily loss rejection | **PASS** | `LiveExecutionRiskValidatorTests.Daily_loss_at_limit_rejects_live_risk` → `LiveExecutionRiskValidator.Validate` → `PortfolioRiskManager.Assess(..., paperTrading: false)` |
| Capital rejection | **PASS** | `LiveExecutionRiskValidatorTests.Capital_budget_exhausted_rejects_live_risk` |
| Position limit rejection | **PASS** | `LiveExecutionRiskValidatorTests.Max_open_positions_rejects_live_risk` |
| Max orders/day rejection | **PASS** | `LiveExecutionRiskValidatorTests.Max_orders_per_day_rejects_before_submit` → stage `DailyOrderLimitExceeded` |
| Emergency stop rejection | **PASS** | `LiveExecutionRiskValidatorTests.Emergency_halt_blocks_confirm_path` → `LiveExecutionCoordinator.ConfirmAsync` |
| Successful auto-live submission | **PASS** | `LiveExecutionRiskValidatorTests.Successful_live_risk_allows_broker_submit` → risk approved then `BrokerSent` |

## Runtime code paths

1. **Live confirm (manual + zone auto)** — `LiveExecutionCoordinator.ConfirmAsync`  
   Order: `Validated` → emergency gate → `LiveExecutionRiskValidator.Validate` → `StructuredRiskDecision` audit → `RiskApproved` or `RiskRejected` / `DailyOrderLimitExceeded` → (on approve) `SaveLiveTrade` → broker loop.

2. **Risk engine** — `LiveExecutionRiskValidator` maps proposal → `StrategyCandidate` (`LiveProposalRiskMapper`) and calls `PortfolioRiskManager.Assess(..., paperTrading: false)` including open live trades from `TradeHistoryStore.ReadLiveTrades(true)`.

3. **Daily cap** — `TradingLimits:MaxOrdersPerDay` via `TradeHistoryStore.CountLiveSubmissionsOnIstDay`.

4. **Emergency stop** — `LiveTradingEmergencyGate` set by ops emergencies; cleared with `CLEAR_TRADING_HALT`. Blocks `ConfirmAsync`, manual live (`ManualTradingService.PlaceLiveOrderAsync`), zone auto defer (`PaperTradingAgent.CreateProposalUnsafe`), and `RequestStrategyPaperTrade`.

## Sample journey timeline (rejection)

```text
Submitted → Validated → StructuredRiskDecision (JSON in reason) → RiskRejected
```

Example structured payload (audit + journey event reason):

```json
{
  "correlationId": "corr-daily-loss",
  "strategy": "VERTICAL_DEBIT_SPREAD",
  "dailyLoss": "FAIL realized ₹-500.00 vs limit ₹500.00",
  "positionCount": "0/30",
  "capitalCheck": "PASS required ₹2,600.00, broker ₹100,000.00",
  "riskDecision": "Rejected",
  "reason": "daily loss limit reached"
}
```

## Sample journey timeline (approved live)

```text
Submitted → Validated → StructuredRiskDecision (Approved) → RiskApproved → BrokerSent → BrokerAccepted → Filled
```

## Configuration

`appsettings.json`:

```json
"TradingLimits": {
  "MaxOrdersPerDay": 25
}
```

Set `0` for unlimited (tests use `0`).
