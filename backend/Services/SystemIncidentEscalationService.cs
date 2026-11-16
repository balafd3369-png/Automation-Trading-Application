using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SystemIncidentEscalationService
{
    private readonly SystemIncidentNotificationStore _store;
    private readonly SystemIncidentNotificationSettingsService _settings;
    private readonly SystemIncidentLifecycleService _lifecycle;
    private readonly ILogger<SystemIncidentEscalationService> _log;

    public SystemIncidentEscalationService(
        SystemIncidentNotificationStore store,
        SystemIncidentNotificationSettingsService settings,
        SystemIncidentLifecycleService lifecycle,
        ILogger<SystemIncidentEscalationService> log)
    {
        _store = store;
        _settings = settings;
        _lifecycle = lifecycle;
        _log = log;
    }

    public void ScheduleForIncident(SystemIncidentRecord incident, DateTimeOffset now)
    {
        var options = _settings.EffectiveOptions;
        if (!options.NotificationsEnabled)
        {
            return;
        }

        if (!_lifecycle.IsActive(incident.Status))
        {
            return;
        }

        var steps = options.ResolveEscalationSteps(incident.Severity);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var scheduled = now.AddMinutes(step.DelayMinutes);
            var idempotency = $"esc|{incident.Id}|{step.Kind}|{i}";
            var job = new IncidentEscalationJobRecord(
                Guid.NewGuid().ToString("N"),
                incident.Id,
                step.Kind,
                i,
                idempotency,
                EscalationJobStatus.Pending,
                scheduled,
                now,
                null);
            if (_store.TryInsertEscalationJob(job))
            {
                _log.LogInformation(
                    "Escalation scheduled for incident {IncidentId} step {StepKind} at {ScheduledUtc}.",
                    incident.Id,
                    step.Kind,
                    scheduled);
            }
        }
    }

    public void CancelAcknowledgementEscalation(string incidentId)
    {
        _store.CancelEscalationJobsForIncident(
            incidentId,
            [EscalationStepKind.UnacknowledgedReminder, EscalationStepKind.SecondaryRecipients]);
        _log.LogInformation("Acknowledgement escalation cancelled for incident {IncidentId}.", incidentId);
    }

    public void CancelAllEscalation(string incidentId)
    {
        _store.CancelEscalationJobsForIncident(incidentId);
        _log.LogInformation("All escalation cancelled for incident {IncidentId}.", incidentId);
    }

}