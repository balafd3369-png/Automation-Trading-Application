using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>Persists alerts only — does not send external notifications.</summary>
public sealed class DatabaseIncidentAlertDispatcher : IIncidentAlertDispatcher
{
    private readonly SystemIncidentStore _store;
    private readonly ILogger<DatabaseIncidentAlertDispatcher> _log;

    public DatabaseIncidentAlertDispatcher(SystemIncidentStore store, ILogger<DatabaseIncidentAlertDispatcher> log)
    {
        _store = store;
        _log = log;
    }

    public Task<IncidentAlertDispatchResult> DispatchAsync(SystemAlertRecord alert, CancellationToken ct)
    {
        try
        {
            var dispatched = alert with
            {
                Status = SystemAlertStatus.Dispatched,
                DispatchedUtc = DateTimeOffset.UtcNow,
                AttemptCount = alert.AttemptCount + 1,
            };
            _store.UpdateAlert(dispatched);
            return Task.FromResult(new IncidentAlertDispatchResult(true, SystemAlertStatus.Dispatched, null));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Alert persistence dispatch failed for alert {AlertId}.", alert.Id);
            return Task.FromResult(new IncidentAlertDispatchResult(false, SystemAlertStatus.Failed, ex.Message));
        }
    }
}