# External uptime monitoring

Use **in addition to** in-app `SystemHealthMonitoringHostedService` (10-minute comprehensive checks).

## DigitalOcean Uptime (or any HTTP checker)

| Setting | Value |
|---------|--------|
| URL | `https://yourdomain.com/api/health` |
| Method | GET |
| Interval | 60–300 seconds |
| Success | HTTP 200, body contains `"status":"ok"` |
| Alert | Email/SMS on 2–3 consecutive failures |

## Degraded detection

When DB probe is enabled (`SystemMonitoring:PublicHealthIncludeDatabaseProbe=true`), expect `"status":"degraded"` if SQLite is unreachable — treat as **critical**.

## Post-deploy smoke

1. `GET /api/health` — unauthenticated.
2. Login → `GET /api/admin/deployment-readiness`.
3. After 10+ minutes → `GET /api/admin/system-health/reports` (admin) shows a comprehensive report timestamp advancing.