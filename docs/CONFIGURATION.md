# Configuration & environment variables

The backend uses the standard ASP.NET Core configuration stack:

`appsettings.json` → `appsettings.{Environment}.json` → **environment variables** → **user secrets** (Development).

Environment variables use `__` (double underscore) for nesting, e.g. `SystemIdentity__AdminPassword`.

## Local development

1. Copy `backend/.env.example` to `backend/.env` (optional; loaded only when `ASPNETCORE_ENVIRONMENT=Development`).
2. Or use user secrets:

```powershell
cd backend
dotnet user-secrets set "SystemIdentity:AdminPassword" "Your-Strong-Password"
dotnet user-secrets set "Broker:ApiKey" "your-mstock-key"
```

3. Run: `dotnet run` from `backend/`.

## Sections

| Section | Purpose |
|---------|---------|
| `SystemIdentity` | Admin account, auth flags, registration mode |
| `Security` | JWT/encryption keys (reserved), cookie defaults |
| `LiveTrading` | Live execution feature flag and timeouts |
| `Trading` | Default paper agent capital / risk limits |
| `TradingPlatform` | e.g. `FuturesEnabled` |
| `Broker` | m.Stock credentials (merged into `MStock` at startup) |
| `MStock` | Legacy broker section (still supported) |
| `Database` | Provider (`Sqlite` / future `PostgreSQL`), paths |
| `ConnectionStrings` | `DefaultConnection` (SQLite `Data Source=...` today) |
| `TradeHistory` | Legacy `DatabasePath` |
| `Cors` | `AllowedOrigins` array |
| `Infrastructure` | HTTP timeouts, SignalR, public API URL |
| `InstrumentLots` | Lot sizes for indices and equity FNO |

## Startup validation

On boot, logs include `DEPLOYMENT CHECK PASS`, `WARNING`, or `FAIL`.

**Production fails startup if:**

- `SystemIdentity:AdminPassword` is missing or default (`ChangeMe-Admin-123!`)
- Auth is not in hosted mode (`RequireAuthentication` / `AllowAnonymousLocalUser`)
- `LiveTrading:Enabled=true` but broker API key (and client/access path) is not configured

**Warnings:** live trading enabled, SQLite in production, empty CORS origins in production.

## Testing

Integration tests override settings via `WebApplicationFactory` (`UseSetting`). No `.env` file is required for CI.

See also: [AZURE-APP-SERVICE-CONFIG.md](./AZURE-APP-SERVICE-CONFIG.md), [DEPLOYMENT-HARDENING.md](./DEPLOYMENT-HARDENING.md).
