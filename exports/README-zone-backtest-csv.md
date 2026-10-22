# Zone backtest CSV

## Spot (signal)
- **SpotSignalInterval:** `5minute` — zone entry/exit rules (B1 / S1 breaks).
- Columns **EntryPrice**, **StopLoss**, **Target**, **PointsPnl** are index spot.

## Options (premium)
- **OptionPremiumInterval:** `15minute` — broker **15m candle close** at entry and exit time (nearest bar), when m.Stock/Breeze is logged in.
- **LONG** → **CE**; **SHORT** → **PE**.
- **ITM** and **OTM** strikes (one step from ATM).
- **ItmPremiumSource** / **OtmPremiumSource:**
  - `BROKER_15M` — real historical 15m close from broker.
  - `MODELED` — fallback estimate when contract or history is missing (older expiries).

Regenerate:

```powershell
.\export-zone-backtest-csv.ps1 -LookbackDays 90
```

Requires broker session on port **45211**.
