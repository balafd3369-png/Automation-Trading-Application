# Phase 1 deployment hardening

## Production configuration

| Setting | Friend testing value |
|---------|----------------------|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `LiveTrading:Enabled` | `false` |
| `SystemIdentity:RequireAuthentication` | `true` |
| `SystemIdentity:AllowAnonymousLocalUser` | `false` |
| `SystemIdentity:RegistrationMode` | `Open` (or `InviteOnly` / `Disabled`) |
| `SystemIdentity:AdminPassword` | **Strong secret** (not `ChangeMe-Admin-123!`) |

Frontend: build with **production** configuration (`requireAuth: true`).

### Admin credentials

Set before first hosted run (user secrets or environment):

```powershell
cd backend
dotnet user-secrets set "SystemIdentity:AdminPassword" "Your-Strong-Password-Here"
# optional override email
dotnet user-secrets set "SystemIdentity:AdminEmail" "you@yourdomain.com"
```

On **Production** startup, the app **fails to start** if `AdminPassword` is missing or still the default.

Development logs a **warning** instead.

## Verification

### Startup logs

Look for lines prefixed with `DEPLOYMENT CHECK`:

- `PASS` — authentication, anonymous disabled, live trading off, admin password OK
- `WARNING` — development-only weak password or live trading enabled
- `FAIL` — production misconfiguration (process exits)

### HTTP endpoints

| Endpoint | Auth | Purpose |
|----------|------|---------|
| `GET /api/platform/features` | Public | `liveTradingEnabled`, auth flags, `registrationMode` |
| `GET /api/platform/deployment-status` | User session | Overall GREEN/YELLOW/RED + checks |
| `GET /api/admin/deployment-readiness` | Admin | Full PASS/WARNING/FAIL audit |

### Live trading

- `POST /api/live-agent/*` returns **403** when `LiveTrading:Enabled=false`.
- UI hides the **Live** trade tab when `liveTradingEnabled` is false.

### Paper agent

- `POST /api/paper-agent/command` and `take-strategy` require **Admin** role.
- Traders use **Manual** paper trading (per-user).

## Deployment checklist

1. Set `LiveTrading:Enabled=false` (default in `appsettings.json` and `appsettings.Production.json`).
2. Set non-default `SystemIdentity:AdminPassword`.
3. Run backend with `Production` environment.
4. Confirm startup `DEPLOYMENT CHECK PASS` lines.
5. `GET /api/admin/deployment-readiness` → overall **GREEN** or **YELLOW** (broker warnings OK).
6. Production Angular build; confirm login required.
7. Two test users: manual paper isolation smoke test.

## Render (Docker Web Service)

- **Root directory:** `backend`
- **Runtime:** Docker (`backend/Dockerfile` → `dotnet TradeWithBeast.dll`)
- **Health check path:** `/api/health` (no auth; exempt in `RequireAuthenticationMiddleware`)
- **Port:** set Render’s `PORT` env var; app binds `http://0.0.0.0:$PORT`, or **45211** locally when `PORT` is unset
- **Required env:** `ASPNETCORE_ENVIRONMENT=Production`, `SystemIdentity__AdminPassword` (non-default)

## Remaining YELLOW items (acceptable for Phase 1)

- Broker disconnected → reconciliation/market feed checks show **WARNING** until OTP login.
- Open registration unless you set `RegistrationMode` to `InviteOnly` or `Disabled`.
