using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SystemIncidentAlertService
{
    private readonly SystemMonitoringOptions _options;
    private readonly SystemIncidentStore _store;
    private readonly IIncidentAlertDispatcher _dispatcher;

    public SystemIncidentAlertService(
        SystemMonitoringOptions options,
        SystemIncidentStore store,
        IIncidentAlertDispatcher dispatcher)
    {
        _options = options;
        _store = store;
        _dispatcher = dispatcher;
    }

    public async Task<SystemAlertRecord?> TryCreateAlertForIncidentAsync(
        SystemIncidentRecord incident,
        CancellationToken ct)
    {
        if (!_options.AlertEngineEnabled)
        {
            return null;
        }

        if (incident.Severity < _options.AlertMinimumSeverity)
        {
            return null;
        }

        var alertFingerprint = $"alert|{incident.Fingerprint}";
        if (_options.AlertDeduplicationEnabled)
        {
            var since = DateTimeOffset.UtcNow.AddSeconds(-_options.AlertCooldownSeconds);
            var recent = _store.GetRecentAlertByFingerprint(alertFingerprint, since);
            if (recent is not null && recent.Status is SystemAlertStatus.Pending or SystemAlertStatus.Dispatched)
            {
                return recent with { Status = SystemAlertStatus.Suppressed };
            }
        }

        var alert = new SystemAlertRecord(
            Guid.NewGuid().ToString("N"),
            incident.Id,
            alertFingerprint,
            incident.Severity,
            SystemAlertStatus.Pending,
            incident.Title,
            incident.Description,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            0,
            null);
        _store.CreateAlert(alert);

        var dispatch = await _dispatcher.DispatchAsync(alert, ct);
        if (!dispatch.Recorded)
        {
            return _store.UpdateAlert(alert with
            {
                Status = SystemAlertStatus.Failed,
                LastError = dispatch.Error,
                AttemptCount = 1,
            });
        }

        return _store.GetAlert(alert.Id);
    }
}