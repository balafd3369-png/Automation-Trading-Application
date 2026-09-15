# Running tests

## Backend (xUnit)

```powershell
cd backend/tests/TradeWithBeast.Tests
dotnet test
```

Covers zone formulas, Black-Scholes, broker symbol maps, and `/api/board` integration endpoints.

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
