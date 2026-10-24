# Instrument type scaffold (additive)

## Guarantee

This scaffold does **not** change:

- Strategy signals or `PaperDecisionEngine` ranking
- `PortfolioRiskManager` assessments
- `ZoneStrategyRollout` phases
- `AutoAdmitTrades` / paper agent loops
- Zone auto live option order placement
- Strategy catalog IDs or engine code mappings

## Feature flag

`appsettings.json`:

```json
"TradingPlatform": {
  "FuturesEnabled": false
}
```

When `FuturesEnabled` is `false`, `LiveExecutionCoordinator` rejects live broker submission for proposals with **FUTURE** legs only. Paper simulation and catalog option live flows are unchanged.

## API

- `GET /api/platform/features` — `{ futuresEnabled, defaultInstrumentType }`
- `GET /api/broker/capabilities` — includes the same fields (additive)

## Regression tests

Run before/after any instrument work:

```bash
cd backend
dotnet test --filter "FullyQualifiedName~StrategyExecutionRegressionTests"
dotnet test --filter "FullyQualifiedName~InstrumentTypeNormalizerTests"
dotnet test --filter "FullyQualifiedName~LiveExecutionCoordinatorTests"
```

```bash
cd frontend
npm test -- --include=**/instrument-type.util.spec.ts
```

Locked contracts:

- 6 catalog strategy IDs (`StrategyExecutionRegressionTests`)
- Zone rollout policy version `2026-10-zone-90d-v1`
- Option live confirm still places orders when futures disabled
- Future live confirm places **no** broker orders when futures disabled

## Frontend

- `core/instrument-type.util.ts` — `InstrumentKind`, `ProductType`, `normalizeInstrumentType()` (default **OPTION**)
- History → paper position cards default missing leg type to **OPTION** (display only)

## Pre-merge checklist

See **[MERGE-VERIFICATION.md](./MERGE-VERIFICATION.md)** for Tests 1–5 (zone auto live, manual live, paper auto, history, future guard).
