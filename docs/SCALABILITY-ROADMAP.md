# Scalability roadmap (Phase 3 — deferred)

**Status:** Planned — **after** real-world / friend-testing validation (v1 hosted).  
**Current score (honest):** ~**4/10** — acceptable for Phase 1; do not optimize prematurely.  
**Related audits:** [ARCHITECTURE-PERFORMANCE-AUDIT.md](./ARCHITECTURE-PERFORMANCE-AUDIT.md), [PHASE2-LIVE-TRADING-READINESS-AUDIT.md](./PHASE2-LIVE-TRADING-READINESS-AUDIT.md), [PHASE2-FRONTEND-PERFORMANCE-AUDIT.md](./PHASE2-FRONTEND-PERFORMANCE-AUDIT.md)

---

## 1. Purpose

Define **when** and **how** to scale Trade With Beast after production learning — without changing v1 scope today. This document is the single plan for “scalability later.”

### What v1 is sized for (today)

| Dimension | Comfortable target | Hard ceiling (current arch) |
|-----------|-------------------|-----------------------------|
| Concurrent hosted users (browsing) | ~5–15 | ~30+ (SQLite + poll load) |
| Concurrent live order submissions | Manual, low rate | ~10 burst (see live audit) |
| Paper / agent automation | Single engine user | One agent process per deployment |
| Broker connectivity | One m.Stock session per API instance | Shared by all users |
| Data store | SQLite file per instance | Not multi-instance safe without shared DB |

---

## 2. Triggers — start Phase 3 when any of these are true

Use **measured** signals from real-world testing (not guesses):

| Trigger | Example threshold | Action |
|---------|-------------------|--------|
| **User count** | >10–15 regular daily users on one host | Plan shared database |
| **SQLite contention** | p95 API latency >500 ms on writes; lock timeouts in logs | PostgreSQL migration |
| **Multi-instance need** | Single CPU >70% sustained market hours; OOM restarts | Horizontal pods + shared state |
| **Live trading pilot** | >3 users with live enabled concurrently | Per-user broker + order queue |
| **Broker rate limits** | 429 / throttling from m.Stock | Central rate limiter + cache |
| **Frontend load** | >20 users polling board | Push feed + reduce poll (partially done Phase 2) |
| **Business** | Paid multi-tenant or >50 seats committed | Full Phase 3 program |

Until a trigger fires, **keep scalability at ~4/10** and invest in **correctness, UX, and friend-test feedback**.

---

## 3. Real-world testing checklist (before scaling spend)

Complete during v1 hosted test; results feed Phase 3 priority.

### 3.1 Operations

- [ ] `DEPLOYMENT CHECK PASS` on Production startup  
- [ ] `GET /api/admin/deployment-readiness` GREEN/YELLOW documented  
- [ ] Persistent volume for `trade-history.db` (Render disk / Azure Files)  
- [ ] Backup/restore drill for DB + identity store  
- [ ] Uptime during market hours (9:15–15:30 IST) for 5+ sessions  

### 3.2 Multi-user

- [ ] 2–5 test accounts: manual paper isolation (orders, history)  
- [ ] Admin vs trader permissions smoke test  
- [ ] Same symbol board load with 3+ browsers open  

### 3.3 Performance baselines (record numbers)

| Metric | How to measure | Baseline (fill in) |
|--------|----------------|-------------------|
| Board poll p95 | DevTools Network, `/api/board` | ___ ms |
| Option chain p95 | `/api/board/option-chain` | ___ ms |
| SQLite write p95 | Server logs / APM (if added) | ___ ms |
| Frontend idle req/min | Network tab on dashboard vs Strategies | ___ /min |
| Memory after 2 h session | Host metrics | ___ MB |

### 3.4 Live (only if enabled for pilot)

- [ ] Manual live order round-trip time  
- [ ] No duplicate orders on double-click / retry  
- [ ] Reconciliation healthy after disconnect  

---

## 4. Phase 3 roadmap (recommended order)

### Stage A — Data layer (foundation)

**Goal:** Safe multi-instance and more concurrent writers.

| Work item | Detail |
|-----------|--------|
| **PostgreSQL** | Replace SQLite for `TradeHistoryStore`, `UserIdentityStore`, `OperationsStore` |
| **Connection pooling** | Npgsql / EF or Dapper with pool; remove global `lock` on hot paths |
| **Migrations** | Versioned schema; one-time SQLite → Postgres export tool |
| **Config** | `Database__Provider=PostgreSQL`, `ConnectionStrings__DefaultConnection` (already stubbed in [CONFIGURATION.md](./CONFIGURATION.md)) |

**Exit criteria:** Two API instances against one Postgres; integration tests green; no file DB on disk.

**Estimated impact:** Scalability **4 → 6/10**.

---

### Stage B — API tier & session state

**Goal:** Horizontal scale on Azure / Render / K8s.

| Work item | Detail |
|-----------|--------|
| **Stateless API** | No local-only state required for requests |
| **Session store** | Redis or DB-backed sessions (if moving beyond bearer tokens in DB) |
| **Sticky sessions** | Only if temporarily needed; prefer external session store |
| **SignalR backplane** | Redis for `MarketChartHub` when multiple instances |
| **Health + metrics** | Prometheus/App Insights: request latency, DB pool, lock wait |

**Exit criteria:** Load test 50 concurrent read-heavy users; chart hub works across 2 nodes.

**Estimated impact:** Scalability **6 → 7/10**.

---

### Stage C — Market data & broker

**Goal:** Fair sharing of broker quotas; optional per-user live.

| Work item | Detail |
|-----------|--------|
| **Market feed service** | Single writer to m.Stock; cache quotes in Redis |
| **Rate limiter** | Token bucket per deployment + per user for order APIs |
| **Per-user broker tokens** | Vault/Key Vault; no shared m.Stock login for all traders |
| **Option chain cache** | TTL cache keyed by underlying+expiry; CDN not applicable for private quotes |

**Exit criteria:** Option chain stable under 20 concurrent users; broker 429 rate near zero.

**Estimated impact:** Scalability **7 → 7.5/10**; live readiness improves.

---

### Stage D — Execution & live trading scale

**Goal:** Ordered, auditable live path (not HFT).

| Work item | Detail |
|-----------|--------|
| **Order queue** | In-memory channel or Redis queue; max in-flight; back-pressure |
| **Idempotency** | Client + server keys on `PlaceOrderAsync` |
| **Fill events** | Webhook or stream instead of `GetOrdersAsync` polling loop |
| **Live coordinator** | Worker process or dedicated service; not timer on web thread |

Reference: [PHASE2-LIVE-TRADING-READINESS-AUDIT.md](./PHASE2-LIVE-TRADING-READINESS-AUDIT.md) TOP 25 list.

**Exit criteria:** Soak test 10 concurrent manual live submits; no SQLite/API collapse.

**Estimated impact:** Manual live **7.5 → 8.5/10**; scalability **7.5 → 8/10** for execution path.

---

### Stage E — Frontend scale (optional parallel track)

**Goal:** Support more users without poll storms.

| Work item | Detail |
|-----------|--------|
| **Market workbench lazy route** | Split from `AppComponent` (largest JS win remaining) |
| **Push updates** | SignalR for board/agent/live book (reduce REST poll) |
| **Virtualize option chain** | CDK virtual scroll |
| **SCSS modularization** | Fix `app.component.scss` budget |

Reference: [PHASE2-FRONTEND-PERFORMANCE-AUDIT.md](./PHASE2-FRONTEND-PERFORMANCE-AUDIT.md).

**Exit criteria:** Initial bundle <180 kB gzip; dashboard idle <15 req/min per user.

---

### Stage F — Multi-tenant & 1000-user vision (long-term)

Only if product requires it:

- Tenant ID on all rows; broker OAuth per tenant  
- Separate strategy engine workers per tenant or fair-share queue  
- Read replicas for history/reporting  
- WAF, DDoS, per-tenant rate limits  
- Compliance / audit log retention policies  

**Target score:** Scalability **8–9/10** (still not HFT).

---

## 5. Architecture target (high level)

```text
                    ┌─────────────────┐
                    │  CDN / Static   │
                    │  Angular SPA    │
                    └────────┬────────┘
                             │ HTTPS
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
        ┌──────────┐  ┌──────────┐  ┌──────────┐
        │ API #1   │  │ API #2   │  │ API #N   │
        │ (stateless)         SignalR + Redis backplane
        └────┬─────┘  └────┬─────┘  └────┬─────┘
             │             │             │
             └─────────────┼─────────────┘
                           ▼
              ┌────────────────────────┐
              │ PostgreSQL (+ pool)      │
              │ Redis (cache, sessions,  │
              │        SignalR, queues)  │
              └────────────┬───────────┘
                           │
              ┌────────────┴────────────┐
              ▼                         ▼
     ┌─────────────────┐      ┌─────────────────┐
     │ Market feed     │      │ Order worker    │
     │ (m.Stock quotes)│      │ (live queue)    │
     └────────┬────────┘      └────────┬────────┘
              │                         │
              └────────────┬────────────┘
                           ▼
                    ┌─────────────┐
                    │  m.Stock    │
                    └─────────────┘
```

---

## 6. What we explicitly defer

| Item | Reason |
|------|--------|
| HFT / sub-second execution | Architecture is seconds + HTTP; different product |
| 500 concurrent live orders | Requires Stage C+D + broker capacity proof |
| Multi-region active-active | Stage F+ |
| Replace m.Stock | Product decision, not scale-only |

---

## 7. Score progression (planning targets)

| Milestone | Scalability | Notes |
|-----------|-------------|--------|
| **Today (v1 test)** | **4/10** | SQLite monolith, shared broker |
| After Stage A (Postgres) | 6/10 | Multi-writer safe |
| After Stage B (horizontal API) | 7/10 | ~50 read-heavy users |
| After Stage C+D (feed + live queue) | 8/10 | Controlled live pilot scale |
| After Stage F (multi-tenant) | 8.5–9/10 | 1000 users “platform” sense, not HFT |

---

## 8. Ownership & docs to update when starting Phase 3

| When starting… | Update |
|----------------|--------|
| Postgres migration | `CONFIGURATION.md`, `AZURE-APP-SERVICE-CONFIG.md`, migration README |
| Multi-instance | `DEPLOYMENT-HARDENING.md`, Docker/Render docs |
| Live queue | `PHASE2-LIVE-TRADING-READINESS-AUDIT.md` (retest scores) |
| Frontend push | `ANGULAR-PERFORMANCE-AUDIT.md` |

---

## 9. Decision log

| Date | Decision |
|------|----------|
| 2026-10-25 | Defer scalability to post real-world testing; v1 focuses on friend test + paper |
| ___ | First trigger observed: ___ |
| ___ | Stage A kickoff: ___ |

---

*This plan does not require code changes until triggers are met. Revisit quarterly or after major user growth.*
