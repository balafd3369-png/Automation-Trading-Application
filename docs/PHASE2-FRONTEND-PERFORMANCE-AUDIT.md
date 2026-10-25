# Phase 2 — Frontend performance audit

**Scope:** User-perceived performance (dashboard / watchlist / chart first), not micro-optimizations.  
**Stack:** Angular 19, standalone components, OnPush on root, hash routing.  
**Baseline:** After Phase 1 lazy routes (`ANGULAR-PERFORMANCE-AUDIT.md`) — initial **~957 kB** raw / **~211 kB** gzip transfer.

---

## Executive summary

The trading UI is still a **single mega-shell** (`AppComponent` ~**4,900** lines TS, ~**2,470** lines HTML, ~**204 kB** SCSS). Feature pages were lazy-loaded, but **market dashboard, watchlist, chart, and most HTTP polling remain on the critical path**. The largest wins for users are: **stop background API work when not visible**, **split the market workbench**, **defer chart + CDN library cost**, **virtualize long lists**, and **shrink shell CSS/change-detection churn**.

---

## 1. AppComponent size and responsibilities

| Metric | Value | Assessment |
|--------|-------|------------|
| `app.component.ts` | ~4,877 lines | **RED** — god component |
| `app.component.html` | ~2,472 lines | **RED** — entire product UI tree |
| `app.component.scss` | ~204 kB / ~8,200 lines | **RED** — exceeds Angular budget (~166 kB built) |
| Change detection | `OnPush` + **`bumpView()` → `markForCheck()`** (~**96+** call sites) | **YELLOW** — correct pattern but very frequent |

**Responsibilities packed into one component:**

- Board / watchlist / pressure rails / symbol search  
- Options chain (CE/PE trees) and index tabs  
- Lightweight Charts mount, indicators, drawings, live SignalR merge  
- Paper / live / history / agent trade terminals  
- Broker OTP hub, theme picker, notifications  
- Navigation sync, lazy feature loading, zone backtest  
- Pagination, filters, trade tickets  

**Impact:** Every poll and chart tick can mark the **entire** shell dirty; parsing and binding cost scales with template size even when only a quote changed.

---

## 2. Market workbench architecture

- **Pattern:** `centerView === 'market'` renders a three-column **workbench** (left rail, chart, right rail) inside `AppComponent`; routes use `EmptyRouteComponent` + hash state (`parseRouterUrl`).
- **Strengths:** Trade/history panes use `@if (centerView === 'trades')` — **not mounted** on dashboard (good for DOM).
- **Weaknesses:** All market logic, chart utilities imports, and SCSS live in the **initial** bundle; no route-level lazy boundary for “dashboard”.

---

## 3. Watchlist rendering

- **Component:** `WatchlistHoverRowComponent` — **OnPush**, small template (Buy/Sell/Chart actions + `ng-content`).
- **Usage:** `@for` over `selling()`, `buying()`, `ceContracts()`, `peContracts()`, etc. with `track` keys (good).
- **Issue:** Each row is a **component instance** with hover chrome; options mode can render **dozens of strikes × 2 sides** — hundreds of nodes + listeners.
- **Issue:** Template calls like `tone(selected)`, `ceContracts()` on parent re-run on every change detection cycle when `markForCheck` fires.

---

## 4. Chart initialization

| Aspect | Behavior |
|--------|----------|
| Library | **CDN** script in `index.html` (blocking parse): `lightweight-charts@4.2.3` from unpkg |
| Init | `mountChart()` in `AppComponent` — destroys/recreates chart on remount; `ResizeObserver` |
| Data | REST poll **every 5 s** (`chartRefreshSec`) + full refresh **90 s**; SignalR via `ChartLiveCoordinator` |
| Warmup | `hydrateChartFromStorageEarly()` in `ngAfterViewInit`; `prefetchIndexChartsInBackground()` fires **up to 4 extra chart API calls** after broker connect |
| Zone | Heavy work often in `runOutsideAngular` (good); still ends with `bumpView()` |

**User impact:** First dashboard visit waits on **fonts + lightweight-charts CDN + Angular bootstrap + first board + first chart** serially.

---

## 5. CSS bundle size

| Asset | Size (approx.) |
|-------|----------------|
| Global `styles.scss` | ~14 kB transfer (built) |
| **`app.component.scss` alone** | **~166 kB** built (budget warning) |
| Feature SCSS | Split into lazy chunks for deferred routes |

**Impact:** Large component styles inflate **initial CSS parse** and style recalc on theme changes (`applyThemePalette` touches chart + DOM).

---

## 6. Angular change detection

- Root: **OnPush** (appropriate).
- **`bumpView()`** used after almost every async update (board, chart, options, agent, live, history).
- Many **template method calls** (formatters, filters, `ceContracts()`, quality labels) — no pure pipes / memoization.
- Lazy features use `NgComponentOutlet` — isolated once loaded (good).

**Gap:** Child widgets in the shell are not all OnPush; parent `markForCheck` still walks a **very large** template.

---

## 7. Polling frequency (concurrent HTTP)

| Source | Interval | Active when |
|--------|----------|-------------|
| `BoardPollStore` | **6 s** (`environment.boardPollMs`) | Always (after start) |
| Chart REST | **5 s** + **90 s** full | Market / chart visible |
| Option chain | **5 s** | `marketKind === 'options'` |
| Paper agent | **4 s** | `state === 'ready'` (**even on dashboard**) |
| Live agent | **4 s** | `ready` + live enabled (**even on dashboard**) |
| Paper trades / history | **5 s** | `centerView === 'trades'` + paper/history tab |
| Operations notifications | **20 s** | Always |
| Zone backtest | **60 s** | Trades + paper tab |

**Typical dashboard session:** **4–6 parallel pollers** (~1.5–2.5 requests/s to backend) plus chart prefetch burst (4× chart `full` depth) after login.

---

## 8. Memory usage

| Retained state | Risk |
|----------------|------|
| `chartDataCache` / persistence | Grows with symbols & timeframes |
| `optionQuotes` Map | Full chain per refresh |
| `boardData` + rail lists | Full Nifty50+ board in memory |
| Lightweight Charts instance | Canvas + series; remount clears but peak on symbol switch |
| Lazy chunks (idle prefetch) | All feature chunks may load in background |

**Estimate:** Options + chart cache + shell DOM — **tens of MB** on long sessions; mobile browsers most affected.

---

## 9. Large DOM rendering

- **Options:** Dual option-chain trees (CE/PE) — largest lists.
- **Trade live terminal:** Nested `@for` (orders, events, positions) when on live tab — heavy but gated by `centerView`.
- **Symbol search picker:** Multiple `@for` lists when open.
- **Paper ledger / history tables:** Large row counts without virtualization in shell.

---

## 10. Repeated API requests

- **Same board snapshot** drives UI but **agent/live/history/chart/option-chain** are **separate endpoints** on independent timers.
- **No shared If-None-Match / ETag** or client coalescing window.
- **Chart prefetch** duplicates work the user may never open (indices other than selected).
- **exhaustMap** on several streams prevents overlap (good) but does not reduce **frequency**.

---

## TOP 10 improvements (largest user-visible speed gains)

Ranked by **user-perceived** impact (dashboard snappiness, tab switches, options scroll, battery/network).

| # | Improvement | Primary win |
|---|-------------|-------------|
| **1** | **Scope HTTP polling to active `centerView` + tab** (pause paper/live/history/options/chart when not visible) | Less jank, faster interactions, lower battery |
| **2** | **Extract `MarketWorkbenchComponent` + lazy route** (move chart, rails, options strip out of `AppComponent`) | Smaller initial JS/CSS; faster first load for non-market routes |
| **3** | **Defer chart library** — dynamic `import()` or `async` script; init chart only when `centerView === 'market'` and host visible | Faster FCP; less main-thread blocking |
| **4** | **Virtualize option chain + long watchlist rails** (`@angular/cdk/scrolling` or similar) | Smooth scroll on BANKNIFTY/SENSEX chains |
| **5** | **Split / trim `app.component.scss`** (layout shell only; per-feature styles with lazy components) | Faster first paint; fix budget violation |
| **6** | **Remove or gate `prefetchIndexChartsInBackground()`** (user-triggered or idle with cap) | Stops post-login API storm |
| **7** | **Memoize hot template data** (`ceContracts`, rails, quote labels) — signals or immutable snapshots updated only when board/chain changes | Fewer CD cycles; smoother quotes |
| **8** | **Consolidate “trading state” polling** (single snapshot endpoint or SignalR hub for agent/live/board deltas) | Fewer round trips; consistent UI |
| **9** | **Font loading strategy** — subset DM Sans/Inter/JetBrains or `font-display: swap` + reduce weights in `index.html` | Faster FCP (text visible sooner) |
| **10** | **Destroy chart + disconnect SignalR on `leaveMarketWorkbench()`** (verify full teardown today) | Memory and CPU when on Strategies/Trades |

---

## Estimated impact (if TOP 4–6 implemented)

Estimates for a **mid-tier mobile / 4G** user landing on `#/market` (not lab micro-benchmarks):

| Metric | Current (post Phase 1 lazy) | After high-impact bundle | Notes |
|--------|---------------------------|---------------------------|--------|
| **Initial bundle (gzip)** | ~211 kB | **~150–175 kB** | Workbench lazy + SCSS split |
| **Initial bundle (raw)** | ~957 kB | **~650–750 kB** | Chart utils + workbench chunk |
| **First Contentful Paint** | Baseline | **−200–500 ms** | Defer charts CDN + fonts |
| **Time to Interactive (dashboard)** | Baseline | **−400–900 ms** | Less JS parse + fewer pollers |
| **Memory (30 min session)** | Baseline | **−20–35%** | Virtual lists + chart teardown |
| **Network (dashboard idle)** | ~1.5–2.5 req/s | **~0.3–0.6 req/s** | View-scoped polling |

*Phase 1 lazy loading already saved ~**40 kB** gzip / ~**323 kB** raw on first load vs monolith.*

---

## Findings by impact tier

### High impact

1. **AppComponent god-object** — blocks maintainability and keeps CD + CSS on critical path.  
2. **Always-on paper/live polling on dashboard** — constant `markForCheck` and backend load.  
3. **Synchronous lightweight-charts CDN in `<head>`** — competes with Angular bootstrap.  
4. **204 kB component SCSS** — style system cost on every theme/market view.  
5. **Options chain DOM size** — scroll/layout jank when chain is large.  
6. **Index chart prefetch burst** — noticeable slowness right after broker connect.

### Medium impact

7. **Chart 5 s REST poll** while SignalR active — redundant work (merge strategy exists but both run).  
8. **Template method calls** — CPU on each bump; signals/memoization would help.  
9. **Operations notification 20 s poll** — global; could coalesce with board.  
10. **Idle prefetch of all lazy chunks** — warms cache but uses memory/bandwidth on metered networks.  
11. **Google Fonts blocking** — multiple families/weights in `index.html`.  
12. **Board 6 s + multiple 4–5 s timers** — not aligned; causes staggered jank.

### Low impact

13. **Watchlist row component overhead** vs virtualized plain rows (small lists).  
14. **`exhaustMap` vs `switchMap` tuning** per stream.  
15. **Further OnPush on minor shell widgets** without reducing bump frequency.  
16. **Micro-optimizing `track` keys** (already mostly correct).

---

## Routing / behavior

No routing changes required for Phase 2 recommendations; workbench extraction should **preserve** `parseRouterUrl` / hash URLs (`#/market`, `#/market/options`, etc.).

---

## Suggested implementation order

1. **Polling guards** (1–2 days, low risk) — immediate UX win.  
2. **Chart defer + prefetch gate** (1–2 days).  
3. **Option chain virtualization** (2–4 days).  
4. **Market workbench extraction** (1–2 weeks, largest structural win).  
5. **SCSS modularization** (parallel with workbench split).

---

## Verification checklist

```powershell
cd frontend
npx ng build --configuration=production
# Initial total ~957 kB; app.component.scss budget warning

# Chrome DevTools: Performance + Network on #/market (throttle Fast 3G)
# Count requests over 60 s idle on dashboard vs #/strategies
```

---

## Phase 2 implementation (polling + chart lifecycle)

**Code:** `app.component.ts` — view-scoped poll guards, `IntersectionObserver` on `#chartHost`, expanded `leaveMarketWorkbench()`, index chart prefetch disabled.

### Request rate model (per 60 s idle)

Assumptions: board **6 s**; chart REST **5 s** when active; paper **4 s**; live **4 s**; option chain **5 s**; ops notifications **20 s**. Live trading enabled where noted.

| User context | Before (req/min) | After (req/min) | Δ |
|--------------|------------------|-----------------|-----|
| **Dashboard (stocks)** | board 10 + paper 15 + live 15 + chart ~12 + ops 3 ≈ **55** | board 10 + chart ~12* + ops 3 ≈ **25** | **~−55%** |
| **Dashboard (stocks), chart off-screen** | ~55 | board 10 + ops 3 ≈ **13** | **~−76%** |
| **Options tab** | ~55 + chain 12 ≈ **67** | board 10 + chain 12 + chart ~12 + ops 3 ≈ **37** | **~−45%** |
| **Strategies / Manual / Ops** | board 10 + paper 15 + live 15 + ops 3 ≈ **43** | board 10 + ops 3 ≈ **13** | **~−70%** |
| **Trades → Paper tab** | ~43 + history 12 + backtest 1 ≈ **56** | board 10 + paper 15 + history 12 + backtest 1 + ops 3 ≈ **41** | **~−27%** (live off) |
| **Trades → Live tab** | ~56 + live 15 | board 10 + paper 15 + live 15 + ops 3 ≈ **43** | **~−23%** |
| **After broker connect (burst)** | + up to **4** chart `full` prefetch | **0** prefetch | **−4** one-time |

\*Chart poll only when `chartHostVisible` and series mounted; SignalR disconnected when leaving market.

### Estimated user-visible impact

| Metric | Estimate |
|--------|----------|
| **HTTP requests (dashboard idle)** | **~45–76% fewer** depending on tab/view |
| **TTI / interaction jank on non-market tabs** | **Moderate** — fewer `markForCheck` from agent/live |
| **Memory leaving market** | **Lower** — chart, ResizeObserver, SignalR hub torn down |
| **FCP** | Unchanged (same initial bundle) |
| **Post-login stall** | **Removed** index chart prefetch burst |

### Behaviors preserved

- Hash routes and navigation unchanged.
- Data refreshes when user opens the matching tab (poll filters pass; `startWith(0)` on merged streams still runs on subscribe — first visit to tab fetches immediately on next timer tick or manual reload subjects).

---

## Document history

| Date | Note |
|------|------|
| 2026-10-25 | Phase 2 frontend audit (read-only) |
| 2026-10-25 | Phase 2 polling + chart lifecycle implemented |

---

*Complements [ANGULAR-PERFORMANCE-AUDIT.md](./ANGULAR-PERFORMANCE-AUDIT.md) (Phase 1 lazy loading).*
