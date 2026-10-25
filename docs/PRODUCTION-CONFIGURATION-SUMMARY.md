# Production configuration — implementation summary

This document is the deliverable checklist for the production configuration and environment-variable system. Details: [CONFIGURATION.md](./CONFIGURATION.md), Azure: [AZURE-APP-SERVICE-CONFIG.md](./AZURE-APP-SERVICE-CONFIG.md).

---

## 1. Files created

| Path |
|------|
| `backend/Configuration/DatabaseOptions.cs` |
| `backend/Configuration/BrokerOptions.cs` |
| `backend/Configuration/TradingOptions.cs` |
| `backend/Configuration/SecurityOptions.cs` |
| `backend/Configuration/CorsOptions.cs` |
| `backend/Configuration/InfrastructureOptions.cs` |
| `backend/Configuration/DatabaseConnectionResolver.cs` |
| `backend/Configuration/AppConfigurationBinder.cs` |
| `backend/Configuration/LocalEnvFileLoader.cs` |
| `backend/.env.example` |
| `backend/appsettings.Testing.json` |
| `docs/CONFIGURATION.md` |
| `docs/AZURE-APP-SERVICE-CONFIG.md` |
| `backend/tests/TradeWithBeast.Tests/Services/DeploymentStartupValidatorTests.cs` |

---

## 2. Files modified

| Path |
|------|
| `backend/Program.cs` |
| `backend/appsettings.json` |
| `backend/appsettings.Development.json` |
| `backend/appsettings.Production.json` |
| `backend/Services/DeploymentStartupValidator.cs` |
| `backend/Services/TradeHistoryStore.cs` |
| `backend/Services/OperationsStore.cs` |
| `backend/Services/UserIdentityStore.cs` |
| `backend/Services/PaperTradingAgent.cs` |
| `backend/Broker/MStockOptions.cs` |
| `.gitignore` |
| `docs/DEPLOYMENT-HARDENING.md` |
| Integration/unit tests (DatabaseOptions ctor) |
| `backend/tests/.../TestWebApplicationFactory.cs` |

---

## 3. Environment variables (standard ASP.NET `__` syntax)

| Variable | Purpose |
|----------|---------|
| `ASPNETCORE_ENVIRONMENT` | Development / Testing / Production |
| `SystemIdentity__AdminPassword` | Platform admin password |
| `SystemIdentity__RequireAuthentication` | Force login for APIs |
| `SystemIdentity__AllowAnonymousLocalUser` | Dev-only anonymous fallback |
| `SystemIdentity__RegistrationMode` | Open / InviteOnly / Disabled |
| `LiveTrading__Enabled` | Real-money execution gate |
| `Broker__ApiKey` | m.Stock API key |
| `Broker__ApiSecret` | Login password → `MStock:Password` |
| `Broker__ClientCode` | User id → `MStock:Username` |
| `Broker__TotpSecret` | TOTP (when used) |
| `Broker__RedirectUrl` | OAuth redirect |
| `Broker__RestBaseUrl` / `Broker__WsUrl` | Broker endpoints |
| `MStock__*` | Legacy (still supported) |
| `ConnectionStrings__DefaultConnection` | SQLite or future PostgreSQL |
| `Database__Provider` | `Sqlite` or `PostgreSQL` (future) |
| `Database__SqlitePath` | SQLite file path |
| `TradeHistory__DatabasePath` | Legacy SQLite path |
| `Trading__DefaultMaxDailyLoss` | Paper agent default |
| `Trading__DefaultMaxOpenPositions` | Paper agent default |
| `Trading__MaxTradesPerDay` | Documented limit (0 = unset) |
| `Cors__AllowedOrigins__0` | CORS allowlist (array indices) |
| `Security__JwtSigningKey` | Reserved |
| `Security__DataEncryptionKey` | Reserved |
| `Security__CookieSecure` / `CookieHttpOnly` / `CookieSameSite` | Cookie defaults |
| `Infrastructure__BrokerHttpTimeoutSeconds` | m.Stock HTTP timeout |
| `Infrastructure__PublicApiBaseUrl` | Public API URL |
| `Infrastructure__SignalREnableDetailedErrors` | SignalR diagnostics |
| `AllowedHosts` | Host filtering |

---

## 4. Startup validations

| Message | When |
|---------|------|
| `DEPLOYMENT CHECK PASS` | Auth flags OK, live off (or broker OK when live on), admin password non-default |
| `DEPLOYMENT CHECK WARNING` | Live on, SQLite in Production, empty CORS in Production, dev weak password |
| `DEPLOYMENT CHECK FAIL` | **Production:** default admin password, bad auth flags, live on without broker credentials |

---

## 5. Azure App Service (no code changes)

Set **Application settings**:

```
ASPNETCORE_ENVIRONMENT=Production
SystemIdentity__AdminPassword=<strong-secret>
LiveTrading__Enabled=false
ConnectionStrings__DefaultConnection=Data Source=/home/site/data/trade-history.db
```

When enabling live trading later, also set `Broker__ApiKey`, `Broker__ApiSecret`, `Broker__ClientCode`, `Broker__TotpSecret` as needed.

Health probe: `/api/health`. Full table: [AZURE-APP-SERVICE-CONFIG.md](./AZURE-APP-SERVICE-CONFIG.md).

---

## 6. Remaining hardcoded / non-secret defaults

| Item | Location | Risk |
|------|----------|------|
| `ChangeMe-Admin-123!` | `SystemIdentityOptions.DefaultAdminPassword` | **Detector only** — Production startup fails if this value is still configured |
| m.Stock public URLs | `appsettings.json` | Not secrets |
| Test admin password | `TestAuthHelper.DefaultAdminPassword` | Tests only |
| Session auth (not JWT) | `SessionAuthenticationMiddleware` | `Security:JwtSigningKey` reserved for future |
| Runtime paper limits | Agent chat commands | Override `Trading` defaults |

**No production API keys or passwords are committed in source.** Use user secrets, `.env` (local, gitignored), or Azure App Settings.

---

## Local quick start

```powershell
cd backend
copy .env.example .env
# Edit .env — set SystemIdentity__AdminPassword=CHANGE_ME
dotnet user-secrets set "SystemIdentity:AdminPassword" "Your-Strong-Password"
dotnet run
```

Frontend continues to use `frontend/src/environments/environment*.ts` for API base URL; point `apiBaseUrl` at your backend (local, Render, or Azure).
