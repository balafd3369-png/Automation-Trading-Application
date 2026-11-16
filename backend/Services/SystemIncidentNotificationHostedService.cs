using TradeWithBeast.Models;
using TradeWithBeast.Services.Coordination;

namespace TradeWithBeast.Services;

public sealed class SystemIncidentNotificationHostedService : BackgroundService
{
    private readonly SystemIncidentNotificationSettingsService _settings;
    private readonly SystemIncidentNotificationProcessor _processor;
    private readonly SystemIncidentNotificationStore _store;
    private readonly SystemIncidentStore _incidents;
    private readonly SystemIncidentNotificationService _notifications;
    private readonly SystemLeaderElectionService _leaders;
    private readonly WorkerHeartbeatRegistry _heartbeats;
    private readonly ILogger<SystemIncidentNotificationHostedService> _log;
    private int _busy;
    private const string WorkerName = nameof(SystemIncidentNotificationHostedService);

    public SystemIncidentNotificationHostedService(
        SystemIncidentNotificationSettingsService settings,
        SystemIncidentNotificationProcessor processor,
        SystemIncidentNotificationStore store,
        SystemIncidentStore incidents,
        SystemIncidentNotificationService notifications,
        SystemLeaderElectionService leaders,
        WorkerHeartbeatRegistry heartbeats,
        ILogger<SystemIncidentNotificationHostedService> log)
    {
        _settings = settings;
        _processor = processor;
        _store = store;
        _incidents = incidents;
        _notifications = notifications;
        _leaders = leaders;
        _heartbeats = heartbeats;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(2, _settings.EffectiveOptions.NotificationWorkerIntervalSeconds));
            try
            {
                if (Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
                {
                    try
                    {
                        await RunCycleAsync(stoppingToken);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _busy, 0);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Notification worker cycle failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        _heartbeats.Register(WorkerName);
        _heartbeats.Pulse(WorkerName, WorkerRunStatus.Running, markSuccessfulExecution: true);
        if (!_leaders.IsLeader(SystemLeaderRoles.NotificationCoordinator))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _settings.ReloadRuntimeOverrides();
        var jobs = _store.ListDueEscalationJobs(now);
        foreach (var job in jobs)
        {
            var incident = _incidents.GetById(job.IncidentId);
            if (incident is null)
            {
                _store.MarkEscalationExecuted(job.Id, now);
                continue;
            }

            _notifications.HandleEscalationJob(job, incident, now);
        }

        _ = await _processor.ProcessDueAsync(now, ct);
    }
}