# Phase 2 — Live Trading Readiness Audit

**Document type:** Principal low-latency trading architecture review (read-only)  
**Application:** Automation-Trading-Application — backend, strategy engine, broker, WebSocket, database, hosted services, execution pipeline  
**Code changes:** None  

**Related:** [ARCHITECTURE-PERFORMANCE-AUDIT.md](./ARCHITECTURE-PERFORMANCE-AUDIT.md), [DEPLOYMENT-HARDENING.md](./DEPLOYMENT-HARDENING.md)

---

## 1. Purpose

Determine whether the application can **safely** support concurrent live order load at:

| Concurrent orders (in-flight / burst) | Verdict | Rationale (summary) |
|--------------------------------------|---------|---------------------|
| **10** | **Conditional YES** | Manual live orders can run in parallel over HTTP; strategy/zone paths are serialized. Broker rate limits and SQLite writes are the limiters. Requires live enabled, armed flow, and soak testing. |
| **50** | **NO (strategy/agent path)** / **High risk (manual burst)** | Zone auto-live is single-flight (`_zoneLiveBusy`); agent has one pending proposal; fill polling uses full order-book HTTP per leg. A burst of 50 manual submits may trip broker throttling and DB lock contention. |
| **100** | **NO** | Same bottlenecks at scale; no order queue, no per-order isolation, shared m.Stock session. |
| **500** | **NO** | Not architected for this; would require external OMS, message bus, and horizontal scale. |

*“Concurrent orders” here means simultaneous live submissions or in-flight broker orders, not paper positions.*

---

## 2. Layer review (10 areas)

### 2.1 Signal generation

| | |
|--|--|
| **Current** | `PaperTradingAgent` timer **3 s initial / 5 s period**; entry scan throttled to **≥5 s** (`_lastDecisionScan`); catalog auto-admit **≥8 s**. Signals from `PaperDecisionEngine` + zone/catalog strategies. |
| **Bottleneck** | Multi-second granularity; `MarketContextService.BuildAsync` can take up to **8 s** for option quotes. |
| **Risk** | **YELLOW** |
| **Latency** | **3–15+ s** from market condition to internal proposal |
| **Throughput** | ~0.2–1 signals/minute per strategy path (not burst-oriented) |
| **Fix** | Event-driven triggers; decouple quote fan-out from tick; cap universe size under load |

**Key locations:** `PaperTradingAgent.cs` (timer, `TickCoreAsync`, `ScanEntriesUnsafe`), `MarketContextService.cs`

---

### 2.2 Strategy execution

| | |
|--|--|
| **Current** | In-process strategies; `Interlocked` `_busy` drops overlapping agent ticks; runs under `TradeHistoryStore.RunAsUser(StrategyEngineUserId)`. |
| **Bottleneck** | Single agent instance; `lock (_gate)` around state; zone live deferred to post-tick `ExecuteZoneProposalAsync`. |
| **Risk** | **YELLOW** |
| **Latency** | Adds **0–5 s** wait if tick is busy |
| **Throughput** | One tick pipeline at a time |
| **Fix** | Separate read model for strategy; optional worker queue for admits |

**Key locations:** `PaperTradingAgent.cs` (`TickAsync`, lines ~362–464), `PaperDecisionEngine.cs`

---

### 2.3 Risk validation

| | |
|--|--|
| **Current** | `PortfolioRiskManager.Assess` — session window, max positions, spread/OI, R:R, naked rules; live margin via `CalculateMarginAsync` for naked shorts in `ConfirmAsync` / manual SELL options. |
| **Bottleneck** | Margin check is **synchronous HTTP** on live confirm path. |
| **Risk** | **GREEN** (logic) / **YELLOW** (live margin latency) |
| **Latency** | **&lt;1 ms** in-process; **+100 ms–2 s** margin API |
| **Throughput** | Fine per trade; not parallelized across a burst |
| **Fix** | Cache margin snapshots with TTL; pre-trade risk service |

**Key locations:** `PortfolioRiskManager.cs`, `LiveExecutionCoordinator.ConfirmAsync`, `ManualTradingService.PlaceLiveOrderAsync`

---

### 2.4 Order routing

| | |
|--|--|
| **Current** | `BrokerConnectionManager` → **m.Stock only**; LIMIT orders with price protection in live coordinator; exchange inferred from instrument type. |
| **Bottleneck** | No smart router, no failover, single session store. |
| **Risk** | **YELLOW** |
| **Latency** | Broker-bound |
| **Throughput** | Limited by broker API quotas |
| **Fix** | Idempotent tags; rate limiter; multi-session only with explicit product design |

**Key locations:** `BrokerConnectionManager.cs`, `LiveExecutionCoordinator.BuildOrder`, `MStockOrderClient.cs`

---

### 2.5 Broker API calls

| | |
|--|--|
| **Current** | `HttpClient` named `"mstock"`, **35 s timeout** (`Program.cs`). Orders: POST/GET REST. Quotes: `Parallel.ForEachAsync` **max 3** chunks of 12 instruments; option batches with **~75 ms** gap. |
| **Bottleneck** | Every `WaitForTerminalAsync` calls **`GetOrdersAsync`** (full book) every **500 ms** per leg. Reconcile every **2+ s** refreshes positions + orders. |
| **Risk** | **RED** under concurrent live confirms |
| **Latency** | Place: **~100 ms–3 s**; poll loop: **0.5–60 s** per leg |
| **Throughput** | Collapses when N legs × M concurrent trades poll order book |
| **Fix** | Order-status-by-id API; webhooks; circuit breaker; reduce reconcile frequency when idle |

**Key locations:** `Program.cs`, `MStockOrderClient.cs`, `LiveExecutionCoordinator.WaitForTerminalAsync` (~543–557), `MStockAuthClient.cs` (~229–254, option batch loop)

---

### 2.6 Position management

| | |
|--|--|
| **Current** | Live: `RefreshBrokerBookAsync` + reconcile merges broker orders into `TradeHistoryStore`; in-memory `_positions` / `_orders` on coordinator. Paper: in-memory `_positions` on agent. |
| **Bottleneck** | Reconcile skipped if `_busy`; nested `foreach` over all live trades × orders. |
| **Risk** | **YELLOW** |
| **Latency** | **2–5 s** freshness from reconcile timer |
| **Throughput** | O(trades × orders) per reconcile |
| **Fix** | Incremental order updates; broker position stream |

**Key locations:** `LiveExecutionCoordinator.ReconcileAsync`, `RefreshBrokerBookAsync`, `BrokerReconciliationService`

---

### 2.7 Trade history persistence

| | |
|--|--|
| **Current** | SQLite `TradeHistoryStore`; **`Pooling = false`**; **~29** `lock (_gate)` regions; **synchronous** `ExecuteNonQuery` / `ExecuteReader`. |
| **Bottleneck** | Global store lock serializes all users’ writes. |
| **Risk** | **RED** for multi-user concurrent live |
| **Latency** | **1–50+ ms** per operation; tail latency under contention |
| **Throughput** | Low parallel write QPS |
| **Fix** | PostgreSQL + pooling; narrow locks; async ADO; write-behind queue |

**Key locations:** `TradeHistoryStore.cs` (constructor ~68–72, all DB methods under `lock`)

---

### 2.8 Portfolio updates

| | |
|--|--|
| **Current** | Paper PnL/mark from `UpdatePositionsUnsafe` on each tick; live PnL from broker snapshot on reconcile; manual snapshot rebuilds from DB queries. |
| **Bottleneck** | `GetSnapshot` triggers `RefreshManualPaperMarks` and DB reads. |
| **Risk** | **YELLOW** |
| **Latency** | Poll-aligned (**4–5 s** UI) |
| **Throughput** | Degrades with history size |
| **Fix** | Materialized portfolio view; push updates |

**Key locations:** `ManualTradingService.GetSnapshot`, `PaperTradingAgent.UpdatePositionsUnsafe`

---

### 2.9 WebSocket updates

| | |
|--|--|
| **Current** | SignalR **`MarketChartHub`** + `ChartLiveStreamService` — **chart ticks only**. Live agent, positions, orders: **HTTP poll every 4 s** (`app.component.ts` `interval(4000)` + `board.liveAgent()`). |
| **Bottleneck** | No push for order/fill/position; stale UI up to poll interval. |
| **Risk** | **YELLOW** (live ops) |
| **Latency** | **0–4 s** after server state changes |
| **Throughput** | N users × poll rate → REST load |
| **Fix** | Hub groups for live book; SSE for fills |

**Key locations:** `Program.cs` (`MapHub`), `ChartLiveStreamService.cs`, `frontend/src/app/app.component.ts` (~686–697), `chart-stream.service.ts`

---

### 2.10 Background services

| Service | Interval / behavior | Risk |
|---------|---------------------|------|
| `MockMarketService` | ~5 s market tick | **YELLOW** — locks, broker fan-out |
| `PaperTradingAgent` | 3/5 s tick, single-flight `_busy` | **YELLOW** |
| `LiveExecutionCoordinator` | Reconcile **≥2 s**, skip if `_busy` | **YELLOW** |
| `BrokerReconciliationService` | Periodic broker sync | **YELLOW** |

**Issue:** Timers use `CancellationToken.None` in several paths (`ReconcileAsync`, zone live, agent tick) — shutdown and overload behavior is coarse.

**Fix:** `IHostedService` with linked cancellation; dedicated worker for live execution queue.

---

## 3. Anti-pattern & hazard search

| Pattern | Finding | Location | Risk | Latency | Scalability | Recommended fix |
|---------|---------|----------|------|---------|-------------|-----------------|
| `.Result` (blocking) | `GetAwaiter().GetResult()` on async chart | `MockMarketService.cs` ~146 | **YELLOW** | Blocks thread pool on sync chart API | Chart API concurrency | Expose async-only chart endpoint |
| `.Result` (property) | Cached recommendation DTO | `StrategyRecommendationEngine.cs` ~208 | **GREEN** | None | None | N/A (not Task.Result) |
| `.Result` (property) | Zone CSV fields | `ZoneBoardTradeJournalCsv.cs` | **GREEN** | None | None | N/A |
| `.Wait()` | — | *Not found in .cs/.ts* | — | — | — | — |
| `Thread.Sleep` | — | *Not found in backend .cs* | — | — | — | — |
| `lock(` | Heavy use | `TradeHistoryStore` (~29), `MockMarketService` (~43), `PaperTradingAgent` (~17), `LiveExecutionCoordinator` (~22), `MStockAuthClient` (~5), others | **RED/YELLOW** | Lock wait under load | Serializes parallel users | Shorter sections; per-user shards |
| `SemaphoreSlim` | — | *Not used* | — | — | — | Consider for broker API throttle |
| Sync DB | All SQLite access sync under lock | `TradeHistoryStore.cs` | **RED** | Write stalls | **Major** | Async + external DB |
| Blocking HTTP | Async only, but **sync-over-async** chart | `MockMarketService.Chart` | **YELLOW** | Thread pool | Medium | Remove sync wrapper |
| Large `foreach` | Reconcile all live trades × orders | `LiveExecutionCoordinator.ReconcileAsync` ~384–393 | **YELLOW** | Grows with history | CPU per reconcile | Index by broker order id |
| Large `foreach` | Option quote requests (indices + 12 stocks × strikes) | `MarketContextService.BuildAsync` | **YELLOW** | Up to 8 s | Agent tick duration | Shrink universe when live |
| Unbounded queues | Bounded: agent messages **100**, decisions capped; `MarketContextService` `Queue<decimal>` per symbol (bounded by usage) | `PaperTradingAgent` ~1165–1166 | **GREEN** | — | — | Document retention |
| Memory leaks | Singletons + static SignalR ref counts | `ChartLiveStreamService` static dicts | **GREEN/YELLOW** | — | Connection churn | Monitor watch ref counts |

### Serialization gates (concurrency)

| Mechanism | Location | Effect on concurrent orders |
|-----------|----------|----------------------------|
| `_zoneLiveBusy` | `LiveExecutionCoordinator.ExecuteZoneProposalAsync` ~172 | **Only one** zone auto-live execution at a time |
| `_busy` (agent) | `PaperTradingAgent.TickAsync` ~364 | Drops ticks — delays signals |
| `_busy` (reconcile) | `LiveExecutionCoordinator.ReconcileAsync` ~360 | Skips reconcile — stale book |
| Single `_pending` | Paper agent | **One** strategy proposal at a time |
| No global lock on `ConfirmAsync` body | After proposal check | Multiple **manual** live HTTP posts can run in parallel |

---

## 4. Latency & throughput (specific determinations)

### 4.1 Strategy signal → broker order request

| Path | Expected latency (typical) |
|------|----------------------------|
| **Zone auto-live** (armed) | **5–20 s**: wait for agent tick (3–5 s) + context build (0–8 s) + risk + sequential `PlaceOrderAsync` |
| **Manual confirm** (`ConfirmAsync`) | **Human + 0.5–3 s** to first `PlaceOrderAsync` after API call (margin check adds **+0.1–2 s** for naked options) |
| **Manual trading live** (`PlaceLiveOrderAsync`) | **0.2–3 s** from API to broker POST (no wait-for-fill in service) |
| **Paper-only signal** | **3–15 s** to proposal; **no** broker call until live confirm |

**Dominant terms:** agent timer, `MarketContextService` option budget (**8 s**), broker HTTP, not CPU.

### 4.2 Broker acknowledgement → UI update

| Step | Expected latency |
|------|------------------|
| Fill detected in `WaitForTerminalAsync` | **500 ms–order timeout** (poll interval 500 ms) |
| Persist `UpsertLiveOrder` | **1–50 ms** (+ lock wait) |
| Reconcile refresh (optional) | **0–2 s** if timer fires |
| UI sees update | **+0–4 s** (live agent HTTP poll) |

**Typical end-to-end (agent confirm path):** **~1–8 s** after broker reports COMPLETE until operator sees it in UI.

### 4.3 Maximum safe throughput (order of magnitude)

| Mode | Safe sustained rate (engineering estimate, unvalidated) |
|------|-----------------------------------------------------------|
| Strategy / zone live | **&lt;1 completed live trade per minute** (serialization + multi-leg poll) |
| Manual live submit | **~2–10 orders/minute** per deployment if broker allows; burst **~10** with monitoring |
| Paper agent | **Dozens of paper actions/minute** (not broker-limited) |

*Validate with load tests before production.*

### 4.4 Bottleneck summary

| Resource | Bottleneck |
|----------|------------|
| **Database** | SQLite single-file, `Pooling=false`, global `lock` — **primary scale ceiling** |
| **Memory** | Moderate — bounded message lists; live trade lists grow with history |
| **CPU** | Usually not saturated; JSON parse + reconcile loops under heavy history |
| **Network** | **m.Stock HTTP** — order book polling, quote batches, 35 s timeouts mask hangs |

---

## 5. End-to-end live paths (reference)

```
Signal (PaperTradingAgent tick)
  → PaperDecisionEngine / zone admit
  → PortfolioRiskManager
  → Pending proposal (single)
  → [Human Confirm OR Zone auto-live]
  → LiveExecutionCoordinator.ConfirmAsync
  → per leg: PlaceOrderAsync → WaitForTerminalAsync (500ms poll + GetOrdersAsync)
  → TradeHistoryStore (SQLite)
  → ReconcileAsync (timer)
  → GET /api/live-agent (UI poll 4s)
```

Manual live bypasses wait-for-fill in `ManualTradingService` but still hits broker + SQLite.

---

## 6. Multi-user & live safety

- **Auth:** Session-based; live endpoints gated by `LiveTradingEnabledAttribute` + `LiveTrading:Enabled`.
- **Data:** Per-user rows in SQLite; `RunAsUser` for engine.
- **Shared:** One **m.Stock session** per process, shared market feed, **one strategy engine user**.
- **Phase 2 live:** Safe for **small trusted cohort**, **manual or confirmed** trades, **not** for 50+ concurrent live orders or HFT.

---

## 7. Scorecard (/10)

| Score | Value | Notes |
|-------|-------|-------|
| **LIVE TRADING SCORE** | **5.5 / 10** | Strong guardrails (arm, ack, compensation, feature flag); weak at concurrent execution and fill confirmation |
| **SCALABILITY SCORE** | **4.0 / 10** | Monolith + SQLite + shared broker |
| **EXECUTION LATENCY SCORE** | **4.5 / 10** | Seconds-level, poll-based |
| **MULTI USER SCORE** | **5.5 / 10** | DB isolation OK; shared broker and engine |
| **BROKER INTEGRATION SCORE** | **6.0 / 10** | Complete REST surface; no streaming order updates |
| **PRODUCTION READINESS SCORE** | **5.0 / 10** | Good for phased paper + limited live; needs infra hardening for Phase 2 scale |

---

## 8. TOP 25 improvements before enabling `LiveTrading:Enabled`

1. Complete **soak test** with live disabled in prod-like env; enable live only in controlled UAT.
2. Load test **10 concurrent manual live** orders; measure broker errors and p95 SQLite lock time.
3. Replace **`WaitForTerminalAsync`** full-book polling with **order-by-id** status (if API supports).
4. Introduce **global broker API rate limiter** (token bucket per session).
5. **Queue** live `ConfirmAsync` and manual live submits (max in-flight, back-pressure).
6. Migrate **`TradeHistoryStore`** to **PostgreSQL** with connection pooling.
7. Remove **`MockMarketService.Chart()`** sync-over-async (`GetAwaiter().GetResult()`).
8. Push **live positions/orders/fills** over SignalR; reduce 4 s poll dependency.
9. **Per-user broker OAuth** (or documented single-operator policy if intentional).
10. **Idempotency** on `Tag` / client order keys for all `PlaceOrderAsync` calls.
11. **Chaos tests**: timeout mid multi-leg, partial fill, compensation, reconcile failure.
12. **Metrics**: order submit→ack latency, poll iterations, reconcile duration, lock wait.
13. **Alerting** on `EXIT_FAILED`, `TIMEOUT`, reconciliation `FAILED`.
14. **Cap** `MarketContextService` option universe during live hours.
15. **Document** max open live trades and legs per trade in runbook.
16. **Disable zone auto-live** until single-flight queue is replaced with audited worker.
17. **CancellationToken** propagation on all timer-driven live/agent paths.
18. **CORS** restrict to production frontend origin.
19. **Backup/restore** procedure for `trade-history.db` before live go-live.
20. **Pen test** live arm/confirm/admin endpoints.
21. **Runbook** for emergency exit and broker disconnect mid-trade.
22. **Separate** manual live fill reconciliation (manual orders don’t wait for terminal today).
23. **Review** `AllowAnyOrigin` and session cookie flags on hosted URL.
24. **BrokerReconciliationService** SLO: max staleness before block new live submits.
25. **Sign-off checklist**: `DeploymentReadinessService` all green/warning accepted + business ack on scores above.

---

## 9. Go / no-go (Phase 2 live)

| Criterion | Status |
|-----------|--------|
| Feature flags and arm/ack flows | **Go** (with config discipline) |
| 10 concurrent orders | **Conditional go** (manual path + monitoring) |
| 50+ concurrent orders | **No-go** |
| Multi-user live at scale | **No-go** without per-user broker + DB migration |
| HFT / sub-second options | **No-go** |

**Recommendation:** Enable live only for **operator-controlled** Phase 2: **low concurrency**, **confirmed or manual** orders, **single broker session**, with **`LiveTrading:Enabled`** and readiness checks documented in `DEPLOYMENT-HARDENING.md`.

---

## Document history

| Date | Note |
|------|------|
| 2026-10-25 | Phase 2 live trading readiness audit (read-only) |

---

*This audit does not replace broker certification, regulatory approval, or penetration testing.*
