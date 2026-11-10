# Chart display pipeline (permanent rules)

Chart bugs (squashed NIFTY, flickering options, stale cache) came from **many ad‑hoc fixes** in `AppComponent`. All display rules now live in one place.

**Intraday session + live:** see [`CHART-SESSION-LIVE.md`](./CHART-SESSION-LIVE.md) (09:15 densify, viewport, live tail rules).

## Frontend

| Module | Role |
|--------|------|
| `core/chart-display.pipeline.ts` | **Single source of truth**: trim history to live anchor, zone visibility, persist eligibility, candle-only autoscale range |
| `core/chart-ui.util.ts` | Low-level candle math (merge, sanitize, IST session) |
| `core/chart-persistence.service.ts` | localStorage; **`clearAll()`** on broker connect / Refresh |

### Rules (always applied via `prepareChartResponse`)

1. **Anchor** — board verified LTP, else chart last.
2. **History** — drop **prior days** on the wrong price scale; **never** trim away today's 09:15→now densified session (`trimIntradayHistoryToPriceAnchor`).
3. **Zones** — show B2/S1 lines only when within ~4.5% of anchor; autoscale uses **candles only** (`autoscaleInfoProvider`).
4. **Cache** — never save/load snapshots that fail anchor + zone alignment checks.
5. **Options premium** — no index zones; slow HTTP poll; tail updates only.

## Backend

| Module | Role |
|--------|------|
| `Services/ChartBarSanitizer.cs` | `TrimToLiveAnchor` + wick clamp + live tail patch |
| `MockMarketService.SanitizeBars` | Index/stock charts trimmed to `snap.Last` before API response |
| Option charts | Trim/sanitize with **option LTP**, not underlying index |

## Operator checklist

After deploy or broker OTP:

1. **Refresh** chart once (clears bad localStorage).
2. Confirm feed **live** before trusting zones.
3. If zones still far from spot for >2 min, history reload may be pending (backend); chart stays readable without zone lines.

## Session calendar (09:15 – 15:30 IST)

| Constant | Value |
|----------|--------|
| Cash open | **09:15** IST |
| Chart live cutoff | **15:30** IST (intraday bars stop updating) |
| Next session | New chart only after **09:15** on the next trading day when broker data arrives |

### Behaviour

- **`market-session.util.ts`** — `chartSessionIstDateKey()`, `isNseChartLiveWindow()`, persistence key `|sess:YYYY-MM-DD`.
- **Frozen window** (after 15:30 or before 09:15): no live tick merge, no network chart overwrite if the saved session chart is already loaded; board LTP does not re-trim candles.
- **localStorage** — one snapshot per instrument + interval + **session date**; older session keys pruned on app load.
- **Backend** — `NseCashSession.ChartSessionDateKey()`; chart API cache keyed by session; after hours serves last cached session bars without live tail patch.

Indices, options (premium), and equities share the same cash-session constants (F&O charts use the same clock).

---

## Changing behavior

Do **not** add chart scale logic in `app.component.ts`. Extend `chart-display.pipeline.ts` and add tests in `chart-display.pipeline.spec.ts`.
