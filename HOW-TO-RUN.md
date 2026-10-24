# How to Run — Trade with Beast

Cursor-style **pressure board** for NSE indices and stocks. Mock prices work out of the box; connect **m.Stock** for live quotes and charts.

| Service   | Port  | URL |
|-----------|-------|-----|
| Backend   | 45211 | http://127.0.0.1:45211 |
| Frontend  | 45212 | http://127.0.0.1:45212 |

### One-command start (Windows, recommended)

From the repo root (`Automation-Trading-Application`):

```powershell
.\start-app.ps1
```

This stops anything already on **45211/45212** (fixes “DLL locked” and “port already in use”), then opens **two** PowerShell windows (backend + frontend).  
To stop only: `.\stop-app.ps1`

Health check: `http://127.0.0.1:45211/api/health`

---

## Prerequisites

- **.NET 8 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Node.js 20+** and **npm**
- **Windows:** If `dotnet run` fails with an Application Control / `.exe` block, this project already sets `<UseAppHost>false</UseAppHost>` — use `dotnet run` as shown below (no `.exe` host).

Optional for live data:

- m.Stock Type A API key, username, password (stored in user secrets, not in git)

---

## 1. Clone and open

```powershell
git clone https://github.com/balafd3369-png/Automation-Trading-Application.git
cd Automation-Trading-Application
```

---

## 2. Backend (ASP.NET Core 8)

```powershell
cd backend
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet restore
dotnet run --no-launch-profile --urls http://0.0.0.0:45211
```

Verify:

```powershell
curl http://127.0.0.1:45211/api/board
```

You should get JSON with `symbols`, `counts`, and `feed: "mock-1s"`.

### m.Stock credentials (optional, for live quotes)

Store secrets locally (never commit these):

```powershell
cd backend
dotnet user-secrets set "MStock:ApiKey" "YOUR_API_KEY"
dotnet user-secrets set "MStock:Username" "YOUR_MSTOCK_USERNAME"
dotnet user-secrets set "MStock:Password" "YOUR_MSTOCK_PASSWORD"
```

Session token is saved to:

`%APPDATA%\TradeWithBeast\mstock-session.json`

Restart the backend after setting secrets.

### ICICI Breeze credentials (for live quotes / orders)

Register an app at [Breeze API home](https://api.icicidirect.com/apiuser/home), then:

```powershell
cd backend
dotnet user-secrets set "Breeze:AppKey" "YOUR_APP_KEY"
dotnet user-secrets set "Breeze:SecretKey" "YOUR_SECRET_KEY"
```

Restart the backend. ICICI calls use the official NuGet SDK [`com.breeze.breezeconnect`](https://www.nuget.org/packages/com.breeze.breezeconnect) (no Python package required).

In the UI use **Broker → ICICI Direct** (same OTP-style flow as m.Stock).

If you see *“ICICI connect failed / port 45211”* or *404*, you are usually running an **old** API build (e.g. `C:\dev\trade-with-beast-run\TradeWithBeast.dll`). Stop it and run from this repo:

```powershell
cd backend
dotnet build
dotnet run --no-launch-profile --urls http://0.0.0.0:45211
```

Check: `http://127.0.0.1:45211/api/broker/capabilities` should return `breezeOtp: true`.

### If backend DLL is locked

Another `dotnet run` may still hold the DLL. Kill it and retry:

```powershell
taskkill /F /IM "dotnet.exe"
# or taskkill /F /PID <pid from MSB3027 error>
dotnet run --no-launch-profile --urls http://0.0.0.0:45211
```

---

## 3. Frontend (Angular 19)

In a **second terminal**:

```powershell
cd frontend
npm install
npm start
```

Open: **http://127.0.0.1:45212**

The dev server proxies `/api` → `http://127.0.0.1:45211` (see `frontend/proxy.conf.json`).

After UI changes, use **Ctrl+F5** (hard refresh).

---

## 4. Connect m.Stock in the UI

1. Open http://127.0.0.1:45212
2. In the **m.Stock** bar (above the composer), click **Send OTP**
3. Enter the OTP from your registered mobile → **Verify**
4. Status bar should show **m.Stock live** when quotes flow

**No orders are placed.** This app only shows board pressure and charts.

---

## 5. Using the board

| Action | How |
|--------|-----|
| **Stocks mode** | Left activity bar → grid icon |
| **Options mode** | Left activity bar → bar icon (CE/PE rails, index tabs) |
| **Open symbol** | Title bar **Open** or **Ctrl+P** |
| **Chart timeframe** | 1m / 5m / 15m / 1h / 1D toolbar on chart |
| **Draw on chart** | H-line, Trend, Clear (TradingView Lightweight Charts) |

---

## 6. API quick reference

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/board` | Full pressure board (`BoardPollStore`; default **6s** prod, **1s** dev — see `src/environments/`) |
| GET | `/api/board/chart/{name}?interval=5minute` | Candles + zone lines for chart |
| POST | `/api/broker/otp/send` | Request m.Stock OTP |
| POST | `/api/broker/otp/verify` | Body: `{ "otp": "123456" }` |
| GET | `/api/broker/session` | `{ "connected": true/false }` |

Chart `interval` values: `minute`, `5minute`, `15minute`, `60minute`, `day`.

---

## 7. Multi-user friend testing (login required)

Hosted / friend testing uses **session login** (no per-user broker). Default admin is seeded on first run:

- Email: `admin@tradewithbeast.local` (override via `SystemIdentity:AdminEmail`)
- Password: set **`SystemIdentity:AdminPassword`** via user secrets before hosting (default `ChangeMe-Admin-123!` is **blocked on Production startup**)

```powershell
cd backend
dotnet user-secrets set "SystemIdentity:AdminPassword" "Your-Strong-Password-Here"
```

See [docs/DEPLOYMENT-HARDENING.md](docs/DEPLOYMENT-HARDENING.md) for the full checklist and readiness endpoints.

| Setting | Hosted (default) | Local dev without login |
|---------|------------------|-------------------------|
| `SystemIdentity:RequireAuthentication` | `true` | `false` |
| `SystemIdentity:AllowAnonymousLocalUser` | `false` | `true` |
| Frontend `environment.requireAuth` | `true` (production build) | `false` (`environment.development.ts`) |

Legacy anonymous API (old `local-user` paper rows) only works when **both** backend `AllowAnonymousLocalUser` and frontend `requireAuth: false` are set.

After login: **Admin** → `#/admin`, **Trader** → `#/dashboard`, then use Market / Manual for shared quotes and personal paper trades.

---

## 8. Troubleshooting

| Problem | Fix |
|---------|-----|
| Board shows "Could not load board" | Start backend on 45211 first |
| Footer **m.Stock (quotes empty)** | Re-verify OTP; check API key and user secrets |
| Chart shows two fat candles | Restart backend (intraday fallback fix); hard refresh frontend |
| `MSB3027` file locked | Kill old `.NET Host` / `dotnet` process, run backend again |
| Windows blocks `.exe` | Use `dotnet run` with `<UseAppHost>false</UseAppHost>` (already configured) |

---

## 9. Production build (optional)

```powershell
# Backend
cd backend
dotnet publish -c Release -o ./publish

# Frontend
cd frontend
npm run build
# Output in frontend/dist/frontend/browser — serve behind a reverse proxy with /api → backend
```
