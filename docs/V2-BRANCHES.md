# Trade With Beast — v2 (multi-broker)

Version 2 adds **multiple broker integrations** (not only m.Stock). v1 / m.Stock production stays on `release/mstock-production`.

## Branches

| Branch | Purpose |
|--------|---------|
| `dev/v2-multi-broker` | **v2 development** — broker abstraction, per-user broker selection, config and UI for multiple providers. |
| `release/mstock-production` | **v1 m.Stock production** — no multi-broker work here except hotfixes. |
| `dev/bala-trade-with-beast` | v1 feature line until fully frozen; cherry-pick hotfixes into v2 when needed. |

## Workflow

1. Do all v2 work on `dev/v2-multi-broker`.
2. Keep `release/mstock-production` deployable; merge **only** v1 hotfixes from `dev/bala-trade-with-beast`.
3. When v2 is ready, cut `release/v2-multi-broker` from `dev/v2-multi-broker` (not created until first v2 deploy).

## Planned scope (v2)

- Broker provider interface (quotes, session, instruments) with m.Stock as first implementation
- Configuration: active broker per deployment and/or per user where applicable
- UI: broker connect / OTP flows keyed to selected provider
- Tests: contract tests per broker adapter; v1 m.Stock behavior must not regress

## Git

```powershell
cd Automation-Trading-Application
git checkout dev/v2-multi-broker
```

See [V1-BRANCHES.md](./V1-BRANCHES.md) for Phase 1 deploy.
