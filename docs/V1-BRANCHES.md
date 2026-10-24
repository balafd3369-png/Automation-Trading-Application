# Trade With Beast — v1 branches

| Branch | Purpose |
|--------|---------|
| `release/v1-trade-with-beast` | **Deploy from this branch.** Frozen Phase 1 snapshot: auth, deployment hardening, manual paper trading, UI polish. |
| `dev/bala-trade-with-beast` | **Your day-to-day work.** Same snapshot as v1; merge or cherry-pick into release when ready for the next deploy. |

## Deploy checklist (release branch)

1. `git checkout release/v1-trade-with-beast`
2. Backend: `ASPNETCORE_ENVIRONMENT=Production`, set `SystemIdentity:AdminPassword` (user secrets or env). See [DEPLOYMENT-HARDENING.md](./DEPLOYMENT-HARDENING.md).
3. Frontend: `cd frontend && npm ci && npm run build` (production / `requireAuth: true`).
4. Confirm startup `DEPLOYMENT CHECK PASS` and `GET /api/admin/deployment-readiness`.

## Notes

- `LiveTrading:Enabled` defaults to **false**.
- Parent folder `trade with beast` is not the git root; use `Automation-Trading-Application` for all git commands.
