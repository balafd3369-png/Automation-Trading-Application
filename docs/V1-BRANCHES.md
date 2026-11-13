# Trade With Beast — v1 branches

| Branch | Purpose |
|--------|---------|
| `release/mstock-production` | **m.Stock Phase 1 deploy** — URL navigation, option charts, login polish, index chart quote fixes (`dev` merges here). |
| `release/v1-trade-with-beast` | Earlier Phase 1 snapshot (auth, hardening, manual paper). Use `release/mstock-production` for m.Stock go-live. |
| `dev/bala-trade-with-beast` | **Your day-to-day work.** Merge or cherry-pick into `release/mstock-production` when ready to deploy. |

## Deploy checklist (release branch)

1. `git checkout release/mstock-production` and confirm tip matches `dev/bala-trade-with-beast`
2. Backend: `ASPNETCORE_ENVIRONMENT=Production`, set `SystemIdentity:AdminPassword` (user secrets or env). See [DEPLOYMENT-HARDENING.md](./DEPLOYMENT-HARDENING.md).
3. Frontend: `cd frontend && npm ci && npm run build` (production / `requireAuth: true`).
4. Confirm startup `DEPLOYMENT CHECK PASS` and `GET /api/admin/deployment-readiness`.

## Notes

- `LiveTrading:Enabled` defaults to **false**.
- Parent folder `trade with beast` is not the git root; use `Automation-Trading-Application` for all git commands.
- **Version 2 (multi-broker):** [V2-BRANCHES.md](./V2-BRANCHES.md) — use `dev/v2-multi-broker`, not this release line.
