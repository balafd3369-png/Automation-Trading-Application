# Architecture — Trade with Beast

## What this project is

**Trade with Beast** is a trading **pressure board** with a Cursor/VS Code–style UI. It shows where NSE names sit relative to fixed zone lines (B2, B1, S, S1, S2) and labels each symbol **BUYING**, **SELLING**, or **RANGE BOUND**.

This phase is **read-only**: live/mock quotes, zone math, charts, and symbol selection (stocks vs index options). **No auto-orders, no CE/PE execution, no Dual-MA/RSI.**

---

## High-level architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Browser — Angular 19 (port 45212)                              │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────────────────┐ │
│  │ Selling rail │  │ Editor/chart │  │ Buying + range rails   │ │
│  │ (left)       │  │ + m.Stock OTP│  │ (right)                │ │
│  └──────────────┘  └──────────────┘  └────────────────────────┘ │
│         │                  │                    │               │
│         └──────────────────┼────────────────────┘               │
│                            │ GET /api/* (proxy)                 │
└────────────────────────────┼────────────────────────────────────┘
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│  ASP.NET Core 8 API (port 45211)                                │
│  ┌─────────────────┐  ┌──────────────────┐  ┌───────────────┐ │
│  │ BoardController │  │ BrokerAuthController│ │ MockMarket   │ │
│  │ /api/board      │  │ /api/broker/*     │ │ Service       │ │
│  └────────┬────────┘  └────────┬─────────┘  │ (hosted 1s)   │ │
│           │                    │            └───────┬───────┘ │
│           └────────────────────┼────────────────────┘         │
│                                ▼                                │
│                    ┌───────────────────────┐                    │
│                    │ MStockAuthClient      │                    │
│                    │ (REST Type A)         │                    │
│                    └───────────┬───────────┘                    │
└────────────────────────────────┼────────────────────────────────┘
                                 ▼
                    https://api.mstock.trade
                    (when OTP session active)
```

---

## Repository layout

```
Automation-Trading-Application/
├── backend/
│   ├── Broker/           # m.Stock client, index universe, option symbol keys
│   ├── Controllers/      # REST API
│   ├── Formulas/         # Locked zone lines (NiftyZoneFormula, BlackScholes)
│   ├── Models/           # DTOs (board, chart, counts)
│   ├── Services/         # MockMarketService — board tick + chart data
│   └── data/
│       └── mock-board.json   # Seed OHLC when broker offline
├── frontend/
│   └── src/app/
│       ├── app.component.*   # Full IDE board UI + chart
│       └── core/             # board.service, board.models
├── HOW-TO-RUN.md
└── ARCHITECTURE.md
```

---

## Core domain: zone formula

All zone lines come from **yesterday / two-days-ago OHLC + today open**. Nothing is stored in SQL; values are computed on each tick.

| Line | Formula |
|------|---------|
| **B1** | AVERAGE(yesterday High, today Open) |
| **S1** | AVERAGE(yesterday Low, today Open) |
| **B2** | AVERAGE(High 2 days ago, yesterday High) |
| **S2** | AVERAGE(Low 2 days ago, yesterday Low) |
| **S** | AVERAGE(yesterday High, yesterday Low, today Open) |

Implementation: `backend/Formulas/NiftyZoneFormula.cs`

### Pressure marks (v1)

| Condition | Mark |
|-----------|------|
| last ≥ B1 | **BUYING** |
| last ≤ S1 | **SELLING** |
| else | **RANGE BOUND** |

**Tight range** → forced **RANGE BOUND**:

- NIFTY / FINNIFTY: \|B1 − S1\| < 80  
- SENSEX: \|B1 − S1\| < 250  
- Stocks: \|B1 − S1\| < 0.35% of today Open  

---

## Data flow

### 1. Board tick (every 1 second)

```
MockMarketService (IHostedService timer)
    │
    ├─ Has m.Stock session?
    │     YES → RefreshLiveAsync()
    │           → GET quote/ohlc (batched symbols)
    │           → Update Last, Open, tokens per symbol
    │     NO  → MockWalk() random walk on seed prices
    │
    └─ Snapshot() → GET /api/board
          → symbols[] with zones + mark + counts
```

Frontend polls `GET /api/board` every 1s via `BoardService.watch()`.

### 2. Chart request

```
User selects symbol + timeframe (e.g. RELIANCE, 5m)
    │
    ▼
GET /api/board/chart/RELIANCE?interval=5minute
    │
    ├─ m.Stock session + instrument token?
    │     YES → historical candles for that interval
    │     NO  → synthetic intraday bars (session-shaped)
    │           (daily candles are NOT reused on 5m/1m — avoids fat blocks)
    │
    └─ ChartResponse { candles, last, b2..s2 }
          │
          ▼
    Lightweight Charts (CDN) in app.component
          → candlestick series + B2/B1/S/S1/S2 price lines
```

### 3. Broker auth

```
UI: Send OTP → POST /api/broker/otp/send
UI: Verify   → POST /api/broker/otp/verify  { otp }
                    │
                    ▼
              MStockAuthClient.VerifyOtpAsync
                    │
                    ▼
              Token → %APPDATA%\TradeWithBeast\mstock-session.json
                    │
                    ▼
              Subsequent API calls: Authorization: token {api_key}:{access_token}
```

Credentials (API key, username, password) live in **.NET user secrets** (`trade-with-beast-mstock`), not in git.

---

## Frontend structure

| Area | Role |
|------|------|
| **Activity bar** | Switch **Stocks** vs **Options**; open symbol picker |
| **Left rail** | Selling pressure (stocks) or CE chain (options) |
| **Right rail** | Buying + range (stocks) or PE chain (options) |
| **Editor** | Selected symbol headline, zone chips, chart, code-style zone display |
| **Status bar** | Feed source, BUYING/SELLING/RANGE counts, mode (STOCKS/OPTIONS) |
| **m.Stock bar** | OTP login (faster than chat for session setup) |

Charts use **TradingView Lightweight Charts** (`lightweight-charts@4.2.3` in `index.html`), fed by backend candles — not the tradingview.com embed (NSE symbols are blocked there).

### Options mode (selection only)

- User picks underlying (NIFTY, SENSEX, BANKNIFTY, FINNIFTY), expiry, strike, CE/PE  
- Chart uses **underlying** candles; option premium display is derived (live NFO option chain is planned via m.Stock `NFO:` quote keys)  
- **No orders**

---

## Backend services

| Component | Responsibility |
|-----------|----------------|
| **MockMarketService** | Seed load, 1s tick, live/mock prices, board snapshot, chart assembly |
| **MStockAuthClient** | OTP, OHLC quotes, historical candles, session token |
| **IndexUniverse** | Nifty 50 + index names + m.Stock quote aliases |
| **SeedStore** | Reads `data/mock-board.json` |
| **MStockSessionStore** | Persists access token to AppData |

---

## Symbol universe

- **Indices:** NIFTY, SENSEX, BANKNIFTY, FINNIFTY  
- **Stocks:** Union of Nifty 50, Sensex 30, Bank Nifty, Fin Nifty constituents (deduped)  
- **Quote aliases:** e.g. NIFTY → `NSE:NIFTY 50`, SENSEX → `BSE:SENSEX` (see `IndexUniverse.QuoteKeys`)

---

## Security and secrets

| Store | Contents |
|-------|----------|
| User secrets | `MStock:ApiKey`, `Username`, `Password` |
| AppData JSON | Access token after OTP |
| Git | **Never** commit secrets, `.env`, or `appsettings.Development.json` with keys |

`.gitignore` excludes `node_modules/`, `bin/`, `obj/`, `.angular/`, secrets files.

---

## Technology stack

| Layer | Tech |
|-------|------|
| API | ASP.NET Core 8, minimal hosting |
| UI | Angular 19, standalone components, SCSS |
| Chart | TradingView Lightweight Charts 4.x (CDN) |
| Broker | m.Stock REST Type A |
| Storage | JSON seed + local session file only (no database) |

---

## Planned / out of scope (this phase)

- Strategy engine, auto-orders, CE/PE execution  
- Dual-MA, RSI, or replacing zone formulas  
- TradingView.com widget embed for NSE  
- Full live NFO option chain on chart (partial groundwork: `OptionSymbols`, `BlackScholes`, quote-by-key API)

---

## End-to-end user flow

```mermaid
flowchart TD
    A[Open http://127.0.0.1:45212] --> B{Backend up?}
    B -->|No| C[Error pane — retry]
    B -->|Yes| D[Board loads mock or live feed]
    D --> E[Optional: m.Stock OTP]
    E --> F[Live quotes + history]
    D --> G[Pick Stocks or Options]
    G --> H[Select symbol from rails or Open picker]
    H --> I[Chart loads for symbol + timeframe]
    I --> J[Zone lines B2 B1 S S1 S2 on chart]
    J --> K[Read pressure mark on headline and rails]
```

---

## Related docs

- **[HOW-TO-RUN.md](./HOW-TO-RUN.md)** — install, run, OTP, troubleshooting
