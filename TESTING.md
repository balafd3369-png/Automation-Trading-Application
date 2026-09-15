# Running tests

## Backend (xUnit)

```powershell
cd backend/tests/TradeWithBeast.Tests
dotnet test
```

Covers zone formulas, Black-Scholes, broker symbol maps, parser intent safety, market features,
deterministic strategy generation, 2026 option lots/payoffs, portfolio-risk rejection and sizing,
SQLite migration/multi-leg lifecycle/restart recovery, and board/paper-agent integration endpoints.

The integration factory gives every run an isolated SQLite database. If a locally running backend
locks `backend/bin/Debug/net8.0/TradeWithBeast.dll`, stop only that repository's stale `dotnet`
process or run with a separate output path after restore.

## Frontend (Jasmine + Karma)

```powershell
cd frontend
npm test
```

Covers `BoardService` HTTP calls and live chart sync helpers.

## Run all

```powershell
.\run-tests.ps1
```
