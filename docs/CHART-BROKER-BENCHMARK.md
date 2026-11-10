# Chart vs broker benchmarks (step-by-step roadmap)

How we compare to **Kite (ChartIQ/TradingView)**, **Groww**, and **TradingView** embeds, and what we improve in each phase.

## Comparison matrix (intraday index/stock)

| Capability | Kite / TV | Trade With Beast (now) | Target |
|------------|-----------|------------------------|--------|
| Session open **09:15 IST** | Yes | Yes (server + client policy) | Keep |
| Quick range **Today / 1W** | Yes (1D, 5D chips) | **Today · 2D · 1W · 10D** toolbar | Done |
| Session **vertical separators** | Yes (TV native) | **Histogram bands** at day open | Done |
| MAs **per session** (no overnight bleed) | Implicit on TV | **Session-aware** SMA/EMA/BB/RSI | Done |
| Live tick / stream | Yes | SignalR + REST fallback | Tune latency |
| Zone lines (Sabo B1/B2) | Custom studies | **Built-in** | Advantage |
| TV compare link | N/A | **TV compare** button | Done |
| Volume pane | Yes | Yes (intraday histogram) | Phase 2A |
| Order from chart | Kite ChartIQ | Not yet | Phase 3 |
| Multi-layout 4-up | Kite TV | Not yet | Phase 4 |
| Infinite scrollback | Drag chart right | Full history API | Phase 2 |

## Phase 1 — Clarity (shipped in this pass)

1. **Today / 2D / 5D** — `chartSessionSpan` in `prepareChartResponse` (persisted in `localStorage`).
2. **Session break bands** — lightweight-charts histogram at each session open (TradingView workaround pattern).
3. **Session-aware indicators** — `movingAverageSessionAware`, etc. (no SMA jumping across closed market).
4. **Per-day anchor trim** — history trim no longer drops random middle sessions.
5. **Debug checklist** — `CHART-DEBUG-CHECKLIST.md`.

**Default:** **2D** on 15m/5m; **Go live** switches to **Today** and snaps viewport.

## Phase 2A — Trust (shipped)

1. **Volume** histogram under intraday candles (broker volume when present, else range-based estimate).
2. **Session VWAP** (resets 09:15 IST) — toolbar toggle.
3. **1W / 10D** depth chips + extended broker historical lookback.
4. **09:15** bucket alignment unchanged on server `AlignIntradayBars`.

## Phase 2B — Live volume (shipped)

1. Broker **cumulative volume** deltas → per-minute buckets → forming-bar volume on SignalR push.
2. Frontend applies `volume` from live bar updates to histogram + session VWAP.

## Phase 2C — Load on scroll (shipped)

1. Drag chart **left** near the first bar → `GET /api/board/chart/{name}?before={unix}&sessions=10`.
2. Client **prepends** older candles, shifts viewport, grows `historyMaxTradingDays` (no full remount).
3. Initial load stays fast (toolbar **1W/10D** trim); history is **effectively unlimited** while broker data exists.
4. Scroll-back sets **`historyBefore.exhausted`** when broker returns fewer IST sessions than requested → UI shows **beginning of broker history** (no infinite empty drags).

## Phase 3 — Execution UX

1. Trade ticket from crosshair (price + side).
2. SL/target lines from open positions on chart.
3. Export PNG (built-in lightweight-charts helper).

## Phase 4 — Power user

1. Saved layouts (timeframe + indicators + span) per symbol.
2. Multi-chart grid (2×2) for index + option strike.
3. Optional TradingView widget embed for users who insist on TV studies (keep our zones on native chart).

## How to validate after each release

1. **NIFTY 15m · Today** — one session, one open marker, SMA continuous within day only.
2. **NIFTY 5m · 5D** — vertical bands between sessions, no diagonal MA across weekends.
3. **TV compare** — same interval on TradingView should match shape (not necessarily vendor OHLC).
4. Logs: `ChartAsync` (backend) + `[prepareChartResponse]` (frontend) — see `CHART-DEBUG-CHECKLIST.md`.

## Principles (better than brokers for *your* product)

- **Single pipeline** — no ad-hoc scale logic in `AppComponent` (use `chart-display.pipeline.ts`).
- **Broker truth when connected** — skip client densify when API already has 09:15.
- **Zones are first-class** — Sabo levels are always in autoscale context; brokers need custom indicators.
- **Honest session calendar** — 15:30 freeze, post-close no LTP trim (`CHART-SESSION-LIVE.md`).
