# Trade With Beast — Architecture & Performance Audit

**Document type:** Principal low-latency trading systems architecture review (read-only)  
**Application:** Automation-Trading-Application (backend .NET 8 + Angular frontend)  
**Scope:** Trade execution, strategy, broker, market data, risk, persistence, multi-user, scalability, latency  
**Code changes:** None — audit only  

---

## Audit charter (original request)

Perform a **complete** architecture and performance audit of the entire Trading Application.

**DO NOT MODIFY CODE.**

Act as a principal low-latency trading systems architect.

### Areas audited

1. Trade Execution Pipeline  
2. Strategy Engine  
3. Order Routing  
4. Broker Integration  
5. Market Data Processing  
6. Position Tracking  
7. Risk Management  
8. Database Access  
9. Background Services  
10. Multi User Architecture  
11. Authentication & Authorization  
12. WebSocket Processing  
13. Scalability  
14. Memory Usage  
15. CPU Usage  
16. Threading Model  
17. Async/Await Usage  
18. Error Recovery  
19. Logging Overhead  

For every module: **current implementation**, **potential bottlenecks**, **risk level (GREEN/YELLOW/RED)**, **expected latency impact**, **expected throughput impact**, **recommended fixes**.

### Specific determinations

| Question | Summary answer |
|----------|----------------|
| Can this application safely trade live? | Cautiously, small scale only — live path exists with guardrails; not institutional-grade. Phase 1 expects live disabled. |
| 50 concurrent trades? | Paper: plausible within limits. Live: poor — sequential legs, locks, single-flight reconcile. |
| 100 concurrent trades? | Live: no. Paper: marginal. |
| 500 concurrent trades? | No without major redesign. |
| High-frequency option signals? | No — multi-second agent timers and HTTP broker quotes. |
| Broker-grade execution latency? | No — hundreds of ms to seconds (HTTP + 500 ms fill polling). |
| Multi-user live trading? | Partial — per-user history/manual isolation; shared broker session and shared market feed. |
| Scale to 1000 users? | Not on current monolith + SQLite + single broker session. |

---

## Executive summary (stakeholder message)

The application is a **well-structured monolith** suitable for **authenticated paper trading, manual orders, and controlled live** with m.Stock when explicitly enabled. It is **not** a low-latency or high-frequency system. Execution is **HTTP + polling**, strategy runs on **~3–5 second** timers, and persistence is **SQLite with coarse locking**. **Multi-user** works for **data isolation** on history and manual flows, but **market data and broker connectivity are shared per deployment**.

For **friend/production Phase 1**, deploy with **authentication on, live off, health checks** (see `DEPLOYMENT-HARDENING.md`). Before **live money at scale** or **~1000 users**, plan **database upgrade, per-user brokerage, order queues, streaming updates, and load testing** — not incremental features on the current timer/poll loop.

---

## Order lifecycle trace

Trace: **Signal → Validation → Risk → Order Build → Broker API → Acknowledgement → Position Update → UI Update**

| Step | Implementation | Blocking? | Indicative latency |
|------|----------------|-----------|-------------------|
| **Signal** | `PaperTradingAgent` timer (~3–5 s) → `PaperDecisionEngine` + strategies → optional auto-admit | Timer + `lock (_gate)` on agent | **3–5 s+** from market move |
| **Validation** | `PortfolioRiskManager.Assess` (session window, spreads, OI, R:R, naked rules) | In-process | **&lt;1 ms** |
| **Risk** | Same + capital/margin (live adds HTTP margin) | Live: network I/O | **10 ms – 2 s** |
| **Order build** | `LiveExecutionCoordinator.BuildOrder` / `ManualTradingService` | CPU | **&lt;1 ms** |
| **Broker API** | `MStockOrderClient` HTTP (`HttpClient` ~35 s timeout) | **Network I/O** | **100 ms – 3 s+** per call |
| **Acknowledgement** | `WaitForTerminalAsync`: **500 ms** loop + `GetOrdersAsync` until timeout | **Repeated HTTP** | **0.5–60 s** per leg |
| **Position update** | `TradeHistoryStore` SQLite under **`lock (_gate)`** | **Disk + lock** | **1–50 ms** per write (contention increases under load) |
| **UI update** | Angular HTTP polling (board, option chain ~5 s, `exhaustMap`) | HTTP + render | **1–10 s** perceived |

**Blocking hotspots:** broker HTTP, fill polling, SQLite under store lock, sync-over-async in `MockMarketService.Chart()` (`GetAwaiter().GetResult()`), timer callbacks using `CancellationToken.None` on some reconcile paths.

### Key files (reference)

| Area | Paths |
|------|--------|
| Option chain / quotes | `backend/Controllers/BoardController.cs`, `MockMarketService`, `MStockAuthClient` |
| Live execution | `backend/Services/LiveExecutionCoordinator.cs`, `MStockOrderClient` |
| Paper / strategy | `PaperTradingAgent.cs`, `PaperDecisionEngine.cs`, `PortfolioRiskManager.cs` |
| Auth / multi-user | Session middleware, `TradeHistoryStore.cs`, `UserIdentityStore.cs` |
| Hosting / DI | `backend/Program.cs` |
| Shared feed | `MarketDataDistributor.cs` |
| Deployment readiness | `DeploymentReadinessService.cs` |
| Broker routing | `BrokerConnectionManager.cs` |

---

## Anti-pattern search (solution-wide)

| Pattern | Finding |
|---------|---------|
| `.Result` / `.Wait()` | `MockMarketService.Chart()` uses `GetAwaiter().GetResult()` on async path (sync-over-async under load). Limited `.Wait()` elsewhere in core trading path. |
| `Thread.Sleep` | Not found in app source; `Task.Delay` used (e.g. live fill poll **500 ms**, option quote batch gaps **~75 ms**). |
| `lock {}` | Heavy use — `PaperTradingAgent`, `LiveExecutionCoordinator`, `MockMarketService`, `TradeHistoryStore` (~29 lock regions). Contention under concurrent API + timers. |
| Semaphore misuse | `Interlocked` busy flags (`_busy`, `_zoneLiveBusy`) — overlapping reconcile **dropped** rather than queued. |
| Synchronous EF | **No Entity Framework** — SQLite via Microsoft.Data.Sqlite (sync ADO under locks). |
| Long-running loops | Live order wait loop; market quote chunk loops; agent tick can run long if broker is slow. |
| Memory leaks | Timers + singletons + in-memory collections; no obvious unbounded leak; message/history growth needs retention policy. |
| Excessive logging | Moderate; broker warnings on failures; not high-volume tick logging. |

---

## Module-by-module audit

### 1. Trade Execution Pipeline

| Field | Detail |
|-------|--------|
| **Current implementation** | Paper via `PaperTradingAgent`; live via `LiveExecutionCoordinator.ConfirmAsync` (multi-leg sequential, compensation); manual via `ManualTradingService`. |
| **Potential bottlenecks** | Sequential legs; fill polling; coordinator lock; single-flight reconcile. |
| **Risk level** | **YELLOW** (live) / **GREEN** (paper) |
| **Expected latency impact** | Live: **seconds per trade**; paper: **seconds** |
| **Expected throughput impact** | Low concurrent live throughput |
| **Recommended fixes** | Order state machine + broker events; idempotent order IDs; non-blocking persistence; optional message queue |

### 2. Strategy Engine

| Field | Detail |
|-------|--------|
| **Current implementation** | `PaperDecisionEngine` + `IPaperStrategy` implementations; shared timer; `StrategyEngineUserId` in history store. |
| **Potential bottlenecks** | 3–5 s scan interval; `MarketContextService` broker option fan-out. |
| **Risk level** | **YELLOW** |
| **Expected latency impact** | Signal delay **multi-second** |
| **Expected throughput impact** | Few strategy evaluations per second at best |
| **Recommended fixes** | Event-driven triggers; per-tenant engine isolation; cap auto-admit |

### 3. Order Routing

| Field | Detail |
|-------|--------|
| **Current implementation** | Single broker — `BrokerConnectionManager` → m.Stock only. |
| **Potential bottlenecks** | No smart order router; MIS/NRML validation in coordinator. |
| **Risk level** | **GREEN** (simplicity) / **YELLOW** (no failover) |
| **Expected latency impact** | Broker-bound |
| **Expected throughput impact** | Single venue, single session |
| **Recommended fixes** | Router abstraction; failover; rate-limit-aware submission |

### 4. Broker Integration

| Field | Detail |
|-------|--------|
| **Current implementation** | `MStockAuthClient` (quotes), `MStockOrderClient` (orders); HTTP JSON; session in singleton. |
| **Potential bottlenecks** | HTTP latency; quote batching; broker rate limits / non-JSON error bodies. |
| **Risk level** | **YELLOW** |
| **Expected latency impact** | **100 ms – seconds** per API |
| **Expected throughput impact** | Limited by HTTP and broker quotas |
| **Recommended fixes** | Streaming quotes if available; circuit breaker; resilient JSON parsing (partially addressed for option chain) |

### 5. Market Data Processing

| Field | Detail |
|-------|--------|
| **Current implementation** | `MockMarketService` hosted timer (~5 s); broker OHLC batches; `MarketDataDistributor` = one shared feed for all users. |
| **Potential bottlenecks** | Polling not streaming; board refresh under locks. |
| **Risk level** | **YELLOW** |
| **Expected latency impact** | Multi-second board freshness |
| **Expected throughput impact** | Degrades with many concurrent poll clients |
| **Recommended fixes** | Dedicated feed service; Redis/cache fan-out; SignalR push; avoid sync chart wrappers |

### 6. Position Tracking

| Field | Detail |
|-------|--------|
| **Current implementation** | Paper positions in agent memory + SQLite; live from reconcile + `TradeHistoryStore`. |
| **Potential bottlenecks** | Reconcile timer; SQLite lock. |
| **Risk level** | **YELLOW** |
| **Expected latency impact** | Seconds for reconcile visibility |
| **Expected throughput impact** | Write contention on store |
| **Recommended fixes** | Event-sourced positions; read-optimized store; broker position stream |

### 7. Risk Management

| Field | Detail |
|-------|--------|
| **Current implementation** | `PortfolioRiskManager` — session time, max positions, spread/OI, R:R, naked short rules. |
| **Potential bottlenecks** | In-process assess on agent path only. |
| **Risk level** | **GREEN** (paper rules) / **YELLOW** (live — no exchange-grade pre-trade server) |
| **Expected latency impact** | Negligible in-process |
| **Expected throughput impact** | Minimal |
| **Recommended fixes** | Pre-trade risk service; kill switch; real-time exposure aggregation |

### 8. Database Access

| Field | Detail |
|-------|--------|
| **Current implementation** | SQLite `TradeHistoryStore`, `OperationsStore`, `UserIdentityStore`; pooling disabled. |
| **Potential bottlenecks** | Global `lock` on store; synchronous I/O. |
| **Risk level** | **RED** at high concurrency or multi-instance |
| **Expected latency impact** | **1–50+ ms** per operation; spikes under contention |
| **Expected throughput impact** | Severe ceiling with many users |
| **Recommended fixes** | PostgreSQL + pooling; narrower locks; async ADO |

### 9. Background Services

| Field | Detail |
|-------|--------|
| **Current implementation** | `MockMarketService`, `PaperTradingAgent`, `LiveExecutionCoordinator`, `BrokerReconciliationService` — timer-driven. |
| **Potential bottlenecks** | Fire-and-forget tick tasks; overlap dropped via `_busy`. |
| **Risk level** | **YELLOW** |
| **Expected latency impact** | Jitter on timer boundaries |
| **Expected throughput impact** | Skipped ticks under overload |
| **Recommended fixes** | `IHostedService` with cancellation; channel-based workers |

### 10. Multi User Architecture

| Field | Detail |
|-------|--------|
| **Current implementation** | Session auth; per-user DB rows; isolation tests; shared broker session; shared strategy engine user. |
| **Potential bottlenecks** | One m.Stock session per deployment. |
| **Risk level** | **YELLOW** |
| **Expected latency impact** | N/A |
| **Expected throughput impact** | All users share one quote/order session |
| **Recommended fixes** | Per-user broker tokens; tenant-scoped agents |

### 11. Authentication & Authorization

| Field | Detail |
|-------|--------|
| **Current implementation** | Session middleware, admin gates, `LiveTradingEnabledAttribute`, `DeploymentReadinessService`. |
| **Potential bottlenecks** | CORS `AllowAnyOrigin` in `Program.cs` (security, not latency). |
| **Risk level** | **GREEN** when hardened config; **RED** if misconfigured |
| **Expected latency impact** | Low |
| **Expected throughput impact** | Low |
| **Recommended fixes** | Strict CORS; secure admin password; audit admin actions |

### 12. WebSocket Processing

| Field | Detail |
|-------|--------|
| **Current implementation** | SignalR `MarketChartHub` at `/hubs/market-chart` — charts; board mostly HTTP polling. |
| **Potential bottlenecks** | Poll storm from many clients. |
| **Risk level** | **GREEN** (simplicity) / **YELLOW** (stale UI) |
| **Expected latency impact** | UI lags REST poll interval |
| **Expected throughput impact** | HTTP load scales with users × poll rate |
| **Recommended fixes** | Push quotes/fills/positions over hub or SSE |

### 13. Scalability

| Field | Detail |
|-------|--------|
| **Current implementation** | Single-process monolith; singleton services. |
| **Potential bottlenecks** | SQLite file; in-memory agent state; no horizontal split. |
| **Risk level** | **RED** beyond small user count |
| **Expected latency impact** | Increases with load |
| **Expected throughput impact** | Hard ceiling |
| **Recommended fixes** | Stateless API tier, external DB/cache, autoscaling, split read/write |

### 14. Memory Usage

| Field | Detail |
|-------|--------|
| **Current implementation** | Singletons hold positions, messages, market cache. |
| **Potential bottlenecks** | Unbounded history/message growth without retention. |
| **Risk level** | **YELLOW** |
| **Expected latency impact** | GC pressure if lists grow large |
| **Expected throughput impact** | Moderate |
| **Recommended fixes** | Retention policies; bounded in-memory caches |

### 15. CPU Usage

| Field | Detail |
|-------|--------|
| **Current implementation** | Timer-driven work; JSON parse; strategy logic on thread pool. |
| **Potential bottlenecks** | Option chain / quote batching under load. |
| **Risk level** | **GREEN** at small scale / **YELLOW** under heavy quote load |
| **Expected latency impact** | CPU spikes during batch quotes |
| **Expected throughput impact** | Parallel quote cap (~3) helps but broker-bound |
| **Recommended fixes** | Rate limits; cache; offload heavy backtests |

### 16. Threading Model

| Field | Detail |
|-------|--------|
| **Current implementation** | ASP.NET thread pool + async I/O; coarse `lock` on hot paths. |
| **Potential bottlenecks** | Lock contention on `TradeHistoryStore` and market service. |
| **Risk level** | **YELLOW** |
| **Expected latency impact** | Thread pool queueing under lock hold |
| **Expected throughput impact** | Reduced parallel request handling |
| **Recommended fixes** | Shorter critical sections; lock-free reads where safe |

### 17. Async/Await Usage

| Field | Detail |
|-------|--------|
| **Current implementation** | Mostly async for broker; exceptions: sync `Chart()`, some `CancellationToken.None` on timers. |
| **Potential bottlenecks** | Sync-over-async can block thread pool threads. |
| **Risk level** | **YELLOW** |
| **Expected latency impact** | Rare stalls on sync chart path |
| **Expected throughput impact** | Reduced under concurrent chart requests |
| **Recommended fixes** | End-to-end async; propagate cancellation |

### 18. Error Recovery

| Field | Detail |
|-------|--------|
| **Current implementation** | Live compensation legs; `BrokerReconciliationService`; broker JSON fallbacks; deployment readiness report. |
| **Potential bottlenecks** | Partial failure mid multi-leg live trade. |
| **Risk level** | **YELLOW** |
| **Expected latency impact** | Recovery paths add seconds |
| **Expected throughput impact** | Blocks coordinator during recovery |
| **Recommended fixes** | Chaos testing; dead-letter queue; runbooks |

### 19. Logging Overhead

| Field | Detail |
|-------|--------|
| **Current implementation** | Standard `ILogger`; warnings on broker failures. |
| **Potential bottlenecks** | Would matter if tick-level logging added. |
| **Risk level** | **GREEN** at current volume |
| **Expected latency impact** | Low |
| **Expected throughput impact** | Low |
| **Recommended fixes** | Structured logging; sampling if volume grows |

---

## Scorecard

| Metric | Score |
|--------|-------|
| **OVERALL** | **6.0 / 10** |
| **LIVE TRADING READINESS** | **50%** |
| **SCALABILITY** | **38%** |
| **LATENCY** | **32%** (vs broker/HFT expectations) |
| **RELIABILITY** | **62%** |
| **MULTI-USER** | **55%** |
| **HIGH-FREQUENCY READINESS** | **12%** |

*Interpretation: Strong **retail paper + ops MVP**; not **production HFT** or **multi-tenant brokerage at scale** without infrastructure changes.*

---

## Top 20 improvements before production deployment

1. Keep **`LiveTrading:Enabled=false`** until signed UAT and compliance sign-off.  
2. Load-test **SQLite** under concurrent users; migrate to **PostgreSQL** with connection pooling.  
3. Remove **`GetAwaiter().GetResult()`** from hot `MockMarketService` paths (e.g. sync `Chart()`).  
4. Replace fill **polling (500 ms)** with broker **order updates / webhooks** where available.  
5. Implement **per-user broker sessions** (not one shared m.Stock login for all users).  
6. Narrow **`TradeHistoryStore` locks**; async writes; batch updates where possible.  
7. **Queue** live order submissions with max in-flight and back-pressure.  
8. **Load test** 50/100 simulated concurrent API users (not only concurrent open trades).  
9. Restrict **CORS** to known frontend origins in production.  
10. **Horizontal scale plan**: external session store or sticky sessions; shared DB — not local SQLite per instance without replication.  
11. Push **positions/orders** to UI via SignalR to reduce poll storm.  
12. **Rate limits** on option-chain and board endpoints (protect m.Stock and CPU).  
13. **Idempotency keys** on all broker place-order calls.  
14. **Chaos tests**: broker timeout mid-leg, partial fill, compensation path.  
15. **Separate** strategy engine tenant from manual users for live trading.  
16. **Metrics**: order latency histogram, reconcile lag, lock wait time.  
17. **Retention policy** for agent messages and audit logs.  
18. **Disaster recovery**: backup `trade-history.db` and identity store.  
19. **Pen test** auth, session fixation, and admin endpoints.  
20. **Document supported scale** (e.g. N users, M open paper trades, live manual only, no HFT).

---

## Live execution detail (fill confirmation)

`LiveExecutionCoordinator.WaitForTerminalAsync` polls every **500 ms**, calls `GetOrdersAsync`, updates history, until terminal status or timeout (`OrderTimeoutSeconds`, clamped 3–60 s). Multi-leg trades execute **sequentially**; exit and compensation paths repeat the same pattern.

Implication: **not** suitable for sub-second or HFT execution; suitable for **discretionary / swing-style** live with human arm/confirm flows when enabled.

---

## Deployment alignment

See **`DEPLOYMENT-HARDENING.md`** for Phase 1 expectations:

- `RequireAuthentication=true`  
- `AllowAnonymousLocalUser=false`  
- `LiveTrading:Enabled=false` for friend testing  
- Non-default admin password  
- Health endpoint `/api/health`  

`DeploymentReadinessService` encodes several of these checks for operational visibility.

---

## Document history

| Date | Note |
|------|------|
| 2026-10-25 | Initial read-only audit consolidated for stakeholder use |

---

*This document is an architecture assessment only. It does not replace formal security audit, regulatory review, or broker certification.*
