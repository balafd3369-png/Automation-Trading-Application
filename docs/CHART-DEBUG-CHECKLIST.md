# Chart debugging checklist

Use this when intraday/day charts look wrong (missing 09:15 open, one day only, gaps, flat lines).

## Backend — `ChartAsync()`

In **Development**, every chart API response logs a line like:

```text
ChartAsync source=core symbol=NIFTY interval=5minute candleCount=... firstTs=... lastTs=... duplicateCount=... gapCount=... invalidOhlc=... sorted=...
```

**Grep** backend console or log for `ChartAsync` and verify:

| Field | Good |
|-------|------|
| `symbol` / `interval` | Matches the request |
| `candleCount` | Non-zero; intraday today usually tens+ bars |
| `firstTs` / `lastTs` | Unix seconds; span covers expected session |
| `duplicateCount` | `0` |
| `gapCount` | Low; spikes = broker merge issue |
| `invalidOhlc` | `0` |
| `sorted` | `True` |

`source` tells you which path served data: `core`, `warm-cache`, `session-frozen-cache`, `timeout-cache`, `timeout-fallback`.

## Frontend — `prepareChartResponse()`

In dev (default on), the browser console prints:

```text
[prepareChartResponse] symbol=... interval=... receivedCandles=... processedCandles=... renderedCandles=...
```

Disable with `localStorage.setItem('chartDiag', '0')`; re-enable with `removeItem` or `'1'`.

Compare **received** vs **processed/rendered**: large drops = client trim/densify/session filter.

## Network

For a broken chart, open DevTools → Network and inspect:

```http
GET /api/board/chart/NIFTY?interval=5minute
```

(or the symbol/interval you selected)

| Question | Backend issue if… | Frontend issue if… |
|----------|-------------------|---------------------|
| Are candles present? | Empty or tiny array | API has data but console **received** is 0 |
| Timestamps sorted? | `sorted=False` or JSON out of order | Received sorted but processed dupes/gaps |
| Duplicates? | `duplicateCount > 0` | `rendDupes > 0` with backend dupes 0 |
| OHLC valid? | `invalidOhlc > 0` | Received valid; chart still wrong after pipeline |

**Rule:** API wrong → fix `MockMarketService` / broker merge (`CHART-SESSION-LIVE.md`). API correct, UI wrong → `chart-display.pipeline.ts` and `chart-ui.util.ts`.

## Quick curl (local)

```powershell
Invoke-RestMethod "http://127.0.0.1:45211/api/board/chart/NIFTY?interval=5minute" | Select-Object name, @{n='bars';e={$_.candles.Count}}, @{n='first';e={$_.candles[0].time}}, @{n='last';e={$_.candles[-1].time}}
```
