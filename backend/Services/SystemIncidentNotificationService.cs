using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SystemIncidentNotificationService
{
    private readonly SystemIncidentNotificationStore _store;
    private readonly SystemIncidentStore _incidents;
    private readonly SystemIncidentNotificationSettingsService _settings;
    private readonly SystemIncidentNotificationRouter _router;
    private readonly IncidentNotificationContentBuilder _content;
    private readonly SystemIncidentEscalationService _escalation;
    private readonly ILogger<SystemIncidentNotificationService> _log;

    public SystemIncidentNotificationService(
        SystemIncidentNotificationStore store,
        SystemIncidentStore incidents,
        SystemIncidentNotificationSettingsService settings,
        SystemIncidentNotificationRouter router,
        IncidentNotificationContentBuilder content,
        SystemIncidentEscalationService escalation,
        ILogger<SystemIncidentNotificationService> log)
    {
        _store = store;
        _incidents = incidents;
        _settings = settings;
        _router = router;
        _content = content;
        _escalation = escalation;
        _log = log;
    }

    public void HandleIncidentUpsert(
        SystemIncidentRecord incident,
        SystemIncidentRecord? previousActive,
        SystemIncidentSeverity? previousSeverity)
    {
        var options = _settings.EffectiveOptions;
        if (!options.NotificationsEnabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (previousActive is null)
        {
            var prior = _incidents.GetLatestByFingerprintExcluding(incident.Fingerprint, incident.Id);
            var type = prior is not null
                && prior.Status is SystemIncidentStatus.Resolved or SystemIncidentStatus.Closed
                ? IncidentNotificationType.Reopened
                : IncidentNotificationType.IncidentDetected;
            QueueLifecycleNotifications(incident, type, now, false, extraLine: null);
            if (type is IncidentNotificationType.IncidentDetected or IncidentNotificationType.Reopened)
            {
                _escalation.ScheduleForIncident(incident, now);
            }

            return;
        }

        if (previousSeverity is not null && incident.Severity > previousSeverity.Value)
        {
            QueueLifecycleNotifications(
                incident,
                IncidentNotificationType.SeverityIncreased,
                now,
                false,
                $"Severity increased from {previousSeverity} to {incident.Severity}.");
        }
    }

    public void HandleLifecycleTransition(SystemIncidentRecord incident, IncidentNotificationType type)
    {
        if (!_settings.EffectiveOptions.NotificationsEnabled)
        {
            return;
        }

        QueueLifecycleNotifications(incident, type, DateTimeOffset.UtcNow, false, null);
        switch (type)
        {
            case IncidentNotificationType.Acknowledged:
                _escalation.CancelAcknowledgementEscalation(incident.Id);
                break;
            case IncidentNotificationType.Resolved:
            case IncidentNotificationType.Closed:
                _escalation.CancelAllEscalation(incident.Id);
                break;
        }
    }

    public void HandleEscalationJob(IncidentEscalationJobRecord job, SystemIncidentRecord incident, DateTimeOffset now)
    {
        var options = _settings.EffectiveOptions;
        if (!options.NotificationsEnabled)
        {
            _store.MarkEscalationExecuted(job.Id, now);
            return;
        }

        if (incident.Status is SystemIncidentStatus.Resolved or SystemIncidentStatus.Closed)
        {
            _escalation.CancelAllEscalation(incident.Id);
            _store.MarkEscalationExecuted(job.Id, now);
            return;
        }

        var step = options.ResolveEscalationSteps(incident.Severity)
            .ElementAtOrDefault(job.StepIndex);
        if (step is null)
        {
            return;
        }

        var useSecondary = step.UseSecondaryRecipients || job.StepKind == EscalationStepKind.SecondaryRecipients;
        if (job.StepKind == EscalationStepKind.UnacknowledgedReminder && incident.AcknowledgedUtc is not null)
        {
            _log.LogInformation("Escalation cancelled because incident {IncidentId} is acknowledged.", incident.Id);
            _store.MarkEscalationExecuted(job.Id, now);
            return;
        }

        if (job.StepKind == EscalationStepKind.UnresolvedReminder && incident.Status is SystemIncidentStatus.Resolved or SystemIncidentStatus.Closed)
        {
            _store.MarkEscalationExecuted(job.Id, now);
            return;
        }

        var type = job.StepKind == EscalationStepKind.SecondaryRecipients
            ? IncidentNotificationType.EscalationSecondary
            : IncidentNotificationType.EscalationReminder;
        QueueLifecycleNotifications(
            incident,
            type,
            now,
            useSecondary,
            $"Escalation step {job.StepKind} after {step.DelayMinutes} minutes.");
        _store.MarkEscalationExecuted(job.Id, now);
    }

    public int QueueLifecycleNotifications(
        SystemIncidentRecord incident,
        IncidentNotificationType notificationType,
        DateTimeOffset now,
        bool useSecondaryRecipients,
        string? extraLine)
    {
        var options = _settings.EffectiveOptions;
        if (!options.NotificationsEnabled)
        {
            return 0;
        }

        var url = BuildIncidentUrl(options, incident.Id);
        var content = _content.Build(incident, notificationType, url, extraLine);
        var recipients = _router.ResolveRecipients(options, incident, notificationType, useSecondaryRecipients);
        var queued = 0;
        foreach (var (channel, recipient) in recipients)
        {
            if (TryQueue(options, incident, notificationType, channel, recipient, content, now))
            {
                queued++;
            }
        }

        if (queued > 0)
        {
            _log.LogInformation(
                "Queued {Count} notifications for incident {IncidentId} type {NotificationType}.",
                queued,
                incident.Id,
                notificationType);
        }

        return queued;
    }

    public IncidentNotificationRecord QueueTestNotification(
        IncidentNotificationChannel channel,
        string recipient,
        SystemIncidentRecord incident)
    {
        var options = _settings.EffectiveOptions;
        var now = DateTimeOffset.UtcNow;
        var url = BuildIncidentUrl(options, incident.Id);
        var content = _content.Build(incident, IncidentNotificationType.Test, url, "This is a test notification.");
        var idempotency = $"test|{Guid.NewGuid():N}";
        var record = new IncidentNotificationRecord(
            Guid.NewGuid().ToString("N"),
            incident.Id,
            idempotency,
            channel,
            recipient,
            IncidentNotificationType.Test,
            content.Subject,
            content.Html,
            content.Text,
            IncidentNotificationDeliveryStatus.Pending,
            0,
            now,
            null,
            null,
            null,
            null,
            null);
        if (!_store.TryInsertNotification(record))
        {
            throw new InvalidOperationException("Failed to queue test notification.");
        }

        return record;
    }

    public bool TryQueue(
        IncidentNotificationOptions options,
        SystemIncidentRecord incident,
        IncidentNotificationType notificationType,
        IncidentNotificationChannel channel,
        string recipient,
        (string Subject, string Html, string Text) content,
        DateTimeOffset now)
    {
        var idempotency =
            $"{incident.Id}|{notificationType}|{channel}|{recipient.ToLowerInvariant()}|{incident.Status}|{incident.Severity}";
        if (options.NotificationThrottleSeconds > 0)
        {
            var since = now.AddSeconds(-options.NotificationThrottleSeconds);
            if (_store.HasRecentNotification(idempotency, since))
            {
                return false;
            }
        }

        var record = new IncidentNotificationRecord(
            Guid.NewGuid().ToString("N"),
            incident.Id,
            idempotency,
            channel,
            recipient,
            notificationType,
            content.Subject,
            content.Html,
            content.Text,
            IncidentNotificationDeliveryStatus.Pending,
            0,
            now,
            null,
            null,
            null,
            null,
            null);
        if (!_store.TryInsertNotification(record))
        {
            return false;
        }

        _log.LogInformation(
            "Notification queued {NotificationId} incident {IncidentId} channel {Channel}.",
            record.Id,
            incident.Id,
            channel);
        return true;
    }

    private static string? BuildIncidentUrl(IncidentNotificationOptions options, string incidentId)
    {
        if (string.IsNullOrWhiteSpace(options.IncidentPageUrlTemplate))
        {
            return null;
        }

        return options.IncidentPageUrlTemplate.Replace("{id}", incidentId, StringComparison.OrdinalIgnoreCase);
    }
}