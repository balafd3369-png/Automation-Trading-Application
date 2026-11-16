# System Health Monitoring — Phase 4 (Notification & Escalation)

Phase 4 adds **asynchronous email and Telegram delivery** with **configurable escalation**, built on Phase 3 incidents. It does **not** modify trading execution, brokers, risk gates, or Angular.

## Architecture

- `SystemIncidentNotificationService` — queues lifecycle notifications (detected, severity increase, ack, investigate, resolve, reopen, close, escalation).
- `SystemIncidentEscalationService` — schedules per-severity escalation jobs; cancels on ack / resolve / close.
- `SystemIncidentNotificationStore` — SQLite persistence for notifications, escalation jobs, and non-secret runtime settings.
- `SystemIncidentNotificationProcessor` + `SystemIncidentNotificationHostedService` — claims pending rows, dispatches providers, retries with exponential backoff.
- Providers: `SmtpEmailNotificationProvider` (SMTP / `System.Net.Mail`), `TelegramBotNotificationProvider` (Bot API via `HttpClient`).
- Admin API: `SystemNotificationController` under `/api/admin/*`.

Phase 3 internal alerts (`system_incident_alerts`) remain unchanged.

## Database

| Table | Purpose |
|-------|---------|
| `system_incident_notifications` | Delivery queue/history |
| `system_incident_escalation_jobs` | Scheduled escalation steps |
| `system_notification_runtime_config` | Admin-updated non-secret settings |

Statuses: `Pending`, `Processing`, `Sent`, `Failed`, `RetryScheduled`, `Cancelled`.

## Severity routing (defaults)

| Severity | Email | Telegram |
|----------|-------|----------|
| Critical / High | Primary recipients | Immediate |
| Medium | Primary | If `TelegramOnMediumSeverity` |
| Low | Only if `NotifyOnLowSeverity` | Configurable |

Escalation defaults (overridable via API / config):

- **Critical**: reminders at 2m, secondary at 5m, reminder at 10m.
- **High**: reminder 5m, secondary 15m.
- **Medium**: unresolved reminder 30m.

Acknowledgement stops unacknowledged / secondary escalation. Resolve/close stops all pending escalation.

## Configuration (`IncidentNotification` section)

Secrets (**SMTP password**, **Telegram bot token**) belong in environment variables or user secrets — not committed, not returned by admin APIs.

Key flags:

- `NotificationsEnabled`, `EmailEnabled`, `TelegramEnabled`
- SMTP/Telegram recipient lists (primary + secondary)
- `NotificationThrottleSeconds`, retry limits, worker interval
- `IncidentPageUrlTemplate` — use `{id}` placeholder
- `EscalationPolicies` — per-severity step list

## Admin endpoints

| Method | Path |
|--------|------|
| GET | `/api/admin/notifications` |
| GET | `/api/admin/notifications/{id}` |
| GET | `/api/admin/incidents/{incidentId}/notifications` |
| GET/PUT | `/api/admin/notification-settings` |
| GET/PUT | `/api/admin/escalation-policies` |
| POST | `/api/admin/notifications/test-email` |
| POST | `/api/admin/notifications/test-telegram` |
| POST | `/api/admin/notifications/retry/{id}` |

## Security

- Admin-only (`[AdminAuthorize]`).
- No secrets in API responses or structured logs.
- Parameterized SQL; idempotency unique indexes prevent duplicate queue rows.

## Tests

- `SystemIncidentPhase4Tests` — routing, dedup, escalation cancel, retry, reopen (mock providers).
- `SystemNotificationAdminApiTests` — auth + settings endpoints.

## Known limitations

- Single-instance claiming (no distributed worker lock).
- SMTP/Telegram credentials must be configured externally for real delivery.
- Test endpoints queue messages; delivery still passes through the async worker.