using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SystemIncidentService
{
    private readonly SystemMonitoringOptions _options;
    private readonly SystemIncidentStore _store;
    private readonly SystemIncidentLifecycleService _lifecycle;
    private readonly SystemIncidentEvaluator _evaluator;
    private readonly SystemIncidentAlertService _alerts;
    private readonly SystemIncidentNotificationService _notifications;
    private readonly IProductionSafetyIncidentEvaluator _productionSafety;
    private readonly ILogger<SystemIncidentService> _log;

    public SystemIncidentService(
        SystemMonitoringOptions options,
        SystemIncidentStore store,
        SystemIncidentLifecycleService lifecycle,
        SystemIncidentEvaluator evaluator,
        SystemIncidentAlertService alerts,
        SystemIncidentNotificationService notifications,
        IProductionSafetyIncidentEvaluator productionSafety,
        ILogger<SystemIncidentService> log)
    {
        _options = options;
        _store = store;
        _lifecycle = lifecycle;
        _evaluator = evaluator;
        _alerts = alerts;
        _notifications = notifications;
        _productionSafety = productionSafety;
        _log = log;
    }

    public SystemIncidentRecord? GetById(string id) => _store.GetById(id);

    public IReadOnlyList<SystemIncidentRecord> List(SystemIncidentListQuery query) => _store.List(query);

    public IReadOnlyList<SystemIncidentRecord> ListActive(int limit = 100) => _store.ListActive(limit);

    public IReadOnlyList<SystemIncidentEventRecord> ListEvents(string incidentId, int limit = 200) =>
        _store.ListEvents(incidentId, limit);

    public IReadOnlyList<SystemIncidentNoteRecord> ListNotes(string incidentId, int limit = 200) =>
        _store.ListNotes(incidentId, limit);

    public SystemIncidentStatistics Statistics(DateTimeOffset? fromUtc, DateTimeOffset? toUtc) =>
        _store.BuildStatistics(fromUtc, toUtc);

    public IReadOnlyList<SystemAlertRecord> ListAlerts(SystemAlertListQuery query) => _store.ListAlerts(query);

    public SystemAlertRecord? GetAlert(string id) => _store.GetAlert(id);

    public async Task EvaluateAsync(SystemHealthService health, CancellationToken ct)
    {
        if (!_options.IncidentManagementEnabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var candidates = new List<IncidentEvaluationCandidate>(_evaluator.Evaluate(health));
        candidates.AddRange(_productionSafety.Evaluate());
        foreach (var candidate in candidates)
        {
            if (candidate.IsHealthy)
            {
                await TryAutoResolveAsync(candidate.Fingerprint, now, ct);
                continue;
            }

            if (!_options.IncidentDeduplicationEnabled)
            {
                var created = UpsertWithNotifications(candidate, now);
                await _alerts.TryCreateAlertForIncidentAsync(created, ct);
                continue;
            }

            try
            {
                var incident = UpsertWithNotifications(candidate, now);
                await _alerts.TryCreateAlertForIncidentAsync(incident, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Incident upsert failed for fingerprint {Fingerprint}.", candidate.Fingerprint);
            }
        }
    }

    public SystemIncidentRecord Acknowledge(string incidentId, string adminId)
    {
        var current = _store.GetById(incidentId) ?? throw new InvalidOperationException("Incident not found.");
        _lifecycle.EnsureTransition(current.Status, SystemIncidentStatus.Acknowledged);
        var updated = _store.UpdateStatus(
            incidentId,
            SystemIncidentStatus.Acknowledged,
            SystemIncidentEventTypes.Acknowledged,
            "Incident acknowledged by administrator.",
            adminId,
            DateTimeOffset.UtcNow);
        _notifications.HandleLifecycleTransition(updated, IncidentNotificationType.Acknowledged);
        return updated;
    }

    public SystemIncidentRecord Investigate(string incidentId, string adminId)
    {
        var current = _store.GetById(incidentId) ?? throw new InvalidOperationException("Incident not found.");
        _lifecycle.EnsureTransition(current.Status, SystemIncidentStatus.Investigating);
        var updated = _store.UpdateStatus(
            incidentId,
            SystemIncidentStatus.Investigating,
            SystemIncidentEventTypes.Investigating,
            "Incident marked as investigating.",
            adminId,
            DateTimeOffset.UtcNow);
        _notifications.HandleLifecycleTransition(updated, IncidentNotificationType.Investigating);
        return updated;
    }

    public SystemIncidentRecord Resolve(string incidentId, string adminId, string? message = null)
    {
        var current = _store.GetById(incidentId) ?? throw new InvalidOperationException("Incident not found.");
        _lifecycle.EnsureTransition(current.Status, SystemIncidentStatus.Resolved);
        var updated = _store.UpdateStatus(
            incidentId,
            SystemIncidentStatus.Resolved,
            SystemIncidentEventTypes.Resolved,
            message ?? "Incident resolved by administrator.",
            adminId,
            DateTimeOffset.UtcNow);
        _notifications.HandleLifecycleTransition(updated, IncidentNotificationType.Resolved);
        return updated;
    }

    public SystemIncidentRecord Close(string incidentId, string adminId, string? message = null)
    {
        var current = _store.GetById(incidentId) ?? throw new InvalidOperationException("Incident not found.");
        _lifecycle.EnsureTransition(current.Status, SystemIncidentStatus.Closed);
        var updated = _store.UpdateStatus(
            incidentId,
            SystemIncidentStatus.Closed,
            SystemIncidentEventTypes.Closed,
            message ?? "Incident closed by administrator.",
            adminId,
            DateTimeOffset.UtcNow);
        _notifications.HandleLifecycleTransition(updated, IncidentNotificationType.Closed);
        return updated;
    }

    public SystemIncidentNoteRecord AddNote(string incidentId, string note, string adminId) =>
        _store.AddNote(incidentId, note, adminId, DateTimeOffset.UtcNow);

    public SystemAlertRecord AcknowledgeAlert(string alertId, string adminId)
    {
        var alert = _store.GetAlert(alertId) ?? throw new InvalidOperationException("Alert not found.");
        return _store.UpdateAlert(alert with
        {
            Status = SystemAlertStatus.Acknowledged,
            AcknowledgedUtc = DateTimeOffset.UtcNow,
            AcknowledgedBy = adminId,
        });
    }

    private Task TryAutoResolveAsync(string fingerprint, DateTimeOffset now, CancellationToken ct)
    {
        if (!_options.IncidentAutoResolveEnabled)
        {
            return Task.CompletedTask;
        }

        var active = _store.GetActiveByFingerprint(fingerprint);
        if (active is null)
        {
            return Task.CompletedTask;
        }

        var resolved = _store.UpdateStatus(
            active.Id,
            SystemIncidentStatus.Resolved,
            SystemIncidentEventTypes.Recovered,
            "Underlying health condition recovered.",
            null,
            now);
        _notifications.HandleLifecycleTransition(resolved, IncidentNotificationType.Resolved);
        return Task.CompletedTask;
    }

    private SystemIncidentRecord UpsertWithNotifications(IncidentEvaluationCandidate candidate, DateTimeOffset now)
    {
        var previous = _store.GetActiveByFingerprint(candidate.Fingerprint);
        var previousSeverity = previous?.Severity;
        var incident = _store.CreateOrUpdateOccurrence(candidate, now);
        if (previous is null && incident.OccurrenceCount == 1)
        {
            _notifications.HandleIncidentUpsert(incident, null, null);
        }
        else if (previous is not null)
        {
            _notifications.HandleIncidentUpsert(incident, previous, previousSeverity);
        }

        return incident;
    }
}