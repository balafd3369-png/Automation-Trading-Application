# Angular performance audit — lazy loading

**Date:** 2026-10-25  
**App:** `frontend` (Angular 19, hash routing, standalone components)  
**Constraint:** Existing hash URLs and navigation behavior preserved (`#/market`, `#/trades/history`, etc.).

---

## 1. Architecture before

| Area | Behavior |
|------|----------|
| **Routing** | `app.routes.ts` defined paths; most routes used `EmptyRouteComponent` while **all UI lived in `AppComponent`**. |
| **Bundle** | Single **initial** chunk (`main-*.js`) contained the full trading shell **plus** Strategies, Manual Trading, Trade History, Operations (settings), Intelligence, dashboards, and login. |
| **Eager imports** | `AppComponent` `imports: [...]` listed ~10 heavy standalone feature components. |
| **Router outlet** | Hidden outlet only for login/dashboard/admin-style full-page routes. |

### Feature areas in the initial bundle (before)

| Feature | In initial bundle? | Notes |
|---------|-------------------|--------|
| **Dashboard (market)** | Yes | Chart, board poll, rails — core `AppComponent` |
| **Watchlist** | Yes | `WatchlistHoverRowComponent` + market workbench |
| **Trade History** | Yes | `TradeHistoryDashboardComponent` |
| **Strategies** | Yes | `StrategiesPageComponent` + command center |
| **Manual Trading** | Yes | `ManualTradingPageComponent` |
| **Admin** | Yes | `AdminDashboardComponent` (route import) |
| **Settings / Ops** | Yes | `OperationsSuiteComponent` (`#/operations/:tab`) |
| **User dashboard** | Yes | `UserDashboardComponent` |
| **Login** | Yes | `LoginPageComponent` |
| **Intelligence** | Yes | `SuperIntelligencePageComponent` |

**Measured production build (before):**

| Metric | Value |
|--------|--------|
| Initial raw | **~1.28 MB** |
| Main chunk | **~1.23 MB** |
| Initial transfer (gzip) | **~251 kB** |
| Lazy chunks | **0** (single main) |

---

## 2. Architecture after

### Route-level lazy loading (`loadComponent`)

| Route | Lazy module |
|-------|-------------|
| `#/login` | `login-page.component` |
| `#/dashboard` | `user-dashboard.component` |
| `#/admin` | `admin-dashboard.component` |

Shell routes unchanged: `market`, `market/options`, `strategies`, `manual`, `operations/:tab`, `trades/:tab`, `intelligence` → still `EmptyRouteComponent` + `AppComponent` state sync via `parseRouterUrl`.

### Feature-level lazy loading (dynamic `import()`)

Implemented in `core/app-lazy-features.ts` and `AppComponent` via `NgComponentOutlet`:

| Feature | Chunk (production) | Loaded when |
|---------|-------------------|-------------|
| Strategies | `strategies-page-component` (~78 kB) | `#/strategies` |
| Strategy detail | `strategy-command-center-component` (~34 kB) | `#/strategies/:id` |
| Manual Trading | `manual-trading-page-component` (~47 kB) | `#/manual` |
| Trade History | `trade-history-dashboard-component` (~63 kB) | `#/trades/history` |
| Settings / Operations | `operations-suite-component` (~57 kB) | `#/operations/:tab` |
| Intelligence | `super-intelligence-page-component` (~12 kB) | `#/intelligence` |
| Strategy trades panel | `strategy-trades-panel-component` (~19 kB) | Paper tab + strategy filter |

**Idle prefetch:** `prefetchLazyFeaturesOnIdle()` warms lazy chunks after first paint (does not block first load).

### Still in the initial bundle (by design)

| Feature | Reason |
|---------|--------|
| **Dashboard + watchlist + chart** | Tightly coupled to `AppComponent` workbench (~5k LOC); splitting would require a layout refactor. |
| **Paper / Live / Agent trade panels** | Same shell; lighter than deferred features. |
| **Core services** | Board poll, broker session, chart live, auth |

---

## 3. Measured results (after)

**Production build (`ng build --configuration=production`):**

| Metric | Before | After | Change |
|--------|--------|-------|--------|
| **Initial total (raw)** | ~1.28 MB | **~957 kB** | **~−25% (~323 kB)** |
| **Initial transfer (gzip)** | ~251 kB | **~211 kB** | **~−16% (~40 kB)** |
| **Main chunk** | ~1.23 MB | Split across initial + lazy | — |
| **Lazy chunks** | 0 | **12+** feature/route chunks | On-demand |

**Largest lazy chunks (raw):**

1. Strategies ~78 kB  
2. Trade History ~63 kB  
3. Operations (settings) ~57 kB  
4. Manual Trading ~47 kB  
5. Strategy command center ~34 kB  

---

## 4. Expected user impact

| Scenario | Improvement |
|----------|-------------|
| **First visit to `#/market` (dashboard)** | **~250–320 kB less** JS to parse on critical path; faster TTI on mid devices. |
| **First visit to strategies/history/manual** | Small placeholder then **one** chunk fetch (~12–78 kB gzip ~3–15 kB each). |
| **Return navigation** | Chunks cached by browser — near-instant. |
| **Login / admin / user dashboard** | Not loaded until those routes are opened. |

**Rough first-load improvement (dashboard-only):** **15–25%** less JS download; **20–30%** less parse/compile on initial route (varies by device).  

**Not in scope:** TradingView-style chart weight remains in shell; `app.component.scss` still large (~166 kB).

---

## 5. Files touched

| File | Change |
|------|--------|
| `app.routes.ts` | `loadComponent` for login, dashboard, admin |
| `app.component.ts` | Removed eager feature imports; lazy loader + `NgComponentOutlet` |
| `app.component.html` | Lazy hosts for feature areas |
| `app.component.scss` | `.lazy-route-placeholder` |
| `core/app-lazy-features.ts` | **New** — dynamic imports + idle prefetch |

---

## 6. Routing compatibility

- Hash URLs unchanged (`withHashLocation()`).
- `app-navigation.util.ts` / `parseRouterUrl` unchanged.
- `EmptyRouteComponent` shell routes unchanged.
- Deep links (`#/trades/history`, `#/operations/broker`, `#/strategies/foo`) behave as before.

---

## 7. Follow-up recommendations

1. **Split market workbench** into `MarketWorkbenchComponent` lazy route (largest future win).  
2. **Lazy-load lightweight-charts** (or chart library) only when chart host mounts.  
3. **Split `app.component.scss`** or use per-feature styles to fix budget warning.  
4. **Route-level children** under a thin `AppShellComponent` to move defer logic from `AppComponent`.  
5. Add **bundle budget** per lazy chunk in `angular.json`.

---

## 8. Verification

```powershell
cd frontend
npx ng build --configuration=production
# Confirm "Initial total" ~957 kB and "Lazy chunk files" table in CLI output
```

Optional: analyze `dist/frontend/stats.json` with webpack-bundle-analyzer if enabled in CI.
