# Phase 5 — Angular System Health Monitoring Dashboard

Admin-only monitoring UI integrated under `#/admin/monitoring/*` with `adminGuard`.

## Routes

| Path | Page |
|------|------|
| `/admin/monitoring/overview` | Health overview |
| `/admin/monitoring/components` | Component health |
| `/admin/monitoring/incidents` | Incident list |
| `/admin/monitoring/incidents/:id` | Incident detail |
| `/admin/monitoring/notifications` | Notification center |
| `/admin/monitoring/settings` | Notification settings |
| `/admin/monitoring/escalation` | Escalation policies |
| `/admin/monitoring/analytics` | Historical analytics |

## APIs used

- `/api/admin/system-health/*` (live, components, workers, brokers, market-data, signalr, reports)
- `/api/admin/incidents/*` and `/api/admin/alerts` (via incident flows)
- `/api/admin/notifications/*`, `/api/admin/notification-settings`, `/api/admin/escalation-policies`

## Real-time

Visibility-aware polling via `SystemMonitoringService.pollWhileVisible` (no new SignalR hubs).

## Security

- No SMTP/Telegram secrets in forms or API payloads from the UI.
- Incident descriptions rendered with default Angular interpolation (escaped).

## Limitations

- Dedicated trading/risk/emergency-gate rows appear only when exposed by backend health probes.
- Notification analytics success rate uses the latest loaded sample (not lifetime warehouse).
- Component category groupings are a UI mapping over backend component keys.