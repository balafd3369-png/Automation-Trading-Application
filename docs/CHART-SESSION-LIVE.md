# Intraday chart: session-first live (09:15 IST)

This is the **durable** chart model (TradingView-style cash session). Do not patch `AppComponent` for scale, gaps, or session start — extend the pipeline only.

## Broker vs cache (Zerodha-style)

| Broker connected | Chart source |
|------------------|--------------|
| **Yes** | mStock API only — no localStorage paint, no merge with saved candles, network refresh even after 15:30 |
| **No** | Last saved snapshot if you had one before disconnect; otherwise message: *Connect m.Stock to load charts* |

Rules live in `core/chart-broker.policy.ts`. On OTP connect, saved charts are cleared and a fresh broker load runs.

## Root causes (09:15 gap & “one day only”)

1. **Backend cache after 15:30** — frozen-session and warm chart cache returned raw broker bars (often first 5m ≈ 09:45) without re-densifying. Fix: `RefreshIntradayChartBars` on every cache hit.
2. **`TrimToLiveAnchor` on full series** — could shorten “best run” and drop today’s open buckets before densify. Fix: `TrimIntradayHistoryToLiveAnchor` + `FinalizeIntradayChartBars` (sanitize → densify once).
3. **Frontend after hours** — `sessionFrozen` collapsed the chart to **today only** even when broker was connected. Fix: keep multi-day when `brokerSourceOfTruth`; only offline snapshots stay single-day.
4. **Double densify / wrong open seed** — client re-ran `buildContinuousIntradayChart` on already-densified API candles. Fix: skip client densify when broker connected and 09:15 bucket exists; seed active day from `todayOpen`.

## Goals

| Requirement | How |
|-------------|-----|
| Session starts **09:15** | `alignIntradayBarTimes` + `densifyIstSessionDay` via `buildContinuousIntradayChart` |
| No holes between 5m bars (same day) | Densify missing buckets with prior close (flat) until real OHLC exists |
| History days unchanged by live ticks | Live merge only on bars where `barInActiveChartSession(time)` |
| Login / refresh: no “half day” | `prepareChartResponse` on HTTP merge **and** on mount; merge → **re-run** pipeline |
| Default view: today from open | `activeSessionLogicalRange` + `focusActiveSessionViewport()` (not bare `scrollToRealTime`) |
| Pan left to history | `chartPreserveViewport`; no auto scroll unless user is at live edge and a **new** bar forms |

## Data flow

```
Chart API → mergeChartPayload (merge + prepareChartResponse again)
         → applyChartData → prepareChartResponse
         → mountChart / setData

Board LTP (09:15–15:30) → syncLiveCandle on active session tail only
```

## Modules

- `chart-display.pipeline.ts` — `prepareChartResponse` (align, trim future, densify, trim **prior days** to anchor)
- `chart-ui.util.ts` — bucket math, `buildContinuousIntradayChart`, `trimIntradayHistoryToPriceAnchor`, `activeSessionLogicalRange`
- `market-session.util.ts` — `chartSessionIstDateKey`, `barInActiveChartSession`
- `trading-ui.util.ts` — `syncLiveCandle` (forming bar + single-step new bar)
- Backend — `ChartSessionMerger.AlignIntradayBars`, `TrimFutureIntradayBars`

## Operator

1. **Refresh** once after deploy (clears bad localStorage).
2. **Go live** resets viewport to today 09:15 → now and **unlocks** a user pan/zoom.
3. After pan/zoom, **full broker chart reload is paused** until Go live or Refresh — only the live candle tick updates.
4. Compare interval with [TradingView NSE:NIFTY 5m](https://www.tradingview.com/chart/?symbol=NSE%3ANIFTY&interval=5) — bar **count** for today should match session minutes / 5, not “first API tick”.

## Related

See `CHART-DISPLAY-PIPELINE.md` for anchor, zones, cache, and frozen after-hours behaviour.
