# Azure App Service — configuration

Deploy the **backend** folder as a .NET 8 Web App (Linux or Windows). No code changes are required: set **Application settings** (environment variables) in the Azure portal.

## Required settings

| App setting | Example | Notes |
|-------------|---------|--------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | |
| `SystemIdentity__AdminPassword` | *(strong secret)* | Non-default; app **will not start** if weak |
| `LiveTrading__Enabled` | `false` | Keep `false` until Phase 2 sign-off |
| `WEBSITES_PORT` or `PORT` | `8080` | App binds `0.0.0.0:$PORT` |

## Broker (when using server-stored credentials or live trading)

| App setting | Notes |
|-------------|--------|
| `Broker__ApiKey` | m.Stock Type A API key |
| `Broker__ApiSecret` | Login password (maps to `MStock:Password`) |
| `Broker__ClientCode` | m.Stock user id (maps to `MStock:Username`) |
| `Broker__TotpSecret` | Optional, if used by your login flow |
| `Broker__RedirectUrl` | Optional OAuth redirect |
| `Broker__RestBaseUrl` | Default `https://api.mstock.trade` |
| `Broker__WsUrl` | Default `wss://ws.mstock.trade` |

Legacy names `MStock__ApiKey`, `MStock__Username`, `MStock__Password` still work.

**Production + `LiveTrading__Enabled=true`:** startup **fails** unless `Broker__ApiKey` and client credentials (or access token) are set.

## Database

| App setting | Example |
|-------------|---------|
| `ConnectionStrings__DefaultConnection` | `Data Source=/home/site/data/trade-history.db` |

Mount **Azure Files** or persistent storage at `/home/site/data` so SQLite survives restarts.

Future PostgreSQL:

```
Database__Provider=PostgreSQL
ConnectionStrings__DefaultConnection=Host=...;Database=...;Username=...;Password=...;SSL Mode=Require;
```

(PostgreSQL persistence requires a follow-up migration; options are in place.)

## CORS & frontend

If the Angular app is on a different origin:

```
Cors__AllowedOrigins__0=https://your-frontend.azurewebsites.net
```

When origins are set, only those origins are allowed. When empty, the app allows any origin (backward compatible) but logs a **DEPLOYMENT CHECK WARNING** in Production.

## Optional

| App setting | Purpose |
|-------------|---------|
| `SystemIdentity__RequireAuthentication` | `true` (default) |
| `SystemIdentity__AllowAnonymousLocalUser` | `false` in Production |
| `SystemIdentity__RegistrationMode` | `Open`, `InviteOnly`, `Disabled` |
| `Trading__DefaultMaxDailyLoss` | Paper agent default |
| `Trading__DefaultMaxOpenPositions` | Paper agent default |
| `Infrastructure__BrokerHttpTimeoutSeconds` | Default `35` |
| `Security__JwtSigningKey` | Reserved for future JWT |
| `AllowedHosts` | `*` or your hostname |

## Health check

Configure App Service health probe: **`/api/health`** (no authentication).

## Verify deployment

1. Open **Log stream** — confirm `DEPLOYMENT CHECK PASS` lines.
2. Call `GET https://<app>/api/platform/features`.
3. Admin: `GET /api/admin/deployment-readiness` after login.

## Render / Docker

Same variable names apply. See [DEPLOYMENT-HARDENING.md](./DEPLOYMENT-HARDENING.md) for Render-specific notes.
