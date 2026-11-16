using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services.Coordination;

public sealed class SystemReliabilityCoordinatorHostedService : BackgroundService
{
    private readonly SystemReliabilityOptions _options;
    private readonly SystemInstanceRegistryService _instances;
    private readonly SystemLeaderElectionService _leaders;
    private readonly SystemWorkerRegistryPublisher _workers;
    private readonly SystemRecoveryService _recovery;
    private readonly SystemCoordinationStore _coordination;
    private readonly ILogger<SystemReliabilityCoordinatorHostedService> _log;

    public SystemReliabilityCoordinatorHostedService(
        SystemReliabilityOptions options,
        SystemInstanceRegistryService instances,
        SystemLeaderElectionService leaders,
        SystemWorkerRegistryPublisher workers,
        SystemRecoveryService recovery,
        SystemCoordinationStore coordination,
        ILogger<SystemReliabilityCoordinatorHostedService> log)
    {
        _options = options;
        _instances = instances;
        _leaders = leaders;
        _workers = workers;
        _recovery = recovery;
        _coordination = coordination;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        _coordination.EnsureNotificationLeaseSchemaIfPresent();
        _instances.RegisterStartup();
        _recovery.RunStartupRecovery();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _leaders.TryBecomeLeader(SystemLeaderRoles.NotificationCoordinator);
                _leaders.TryBecomeLeader(SystemLeaderRoles.IncidentEvaluation);
                _leaders.TryBecomeLeader(SystemLeaderRoles.ComprehensiveHealth);
                var isLeader = _leaders.IsLeader(SystemLeaderRoles.NotificationCoordinator)
                    || _leaders.IsLeader(SystemLeaderRoles.IncidentEvaluation);
                _instances.Pulse(isLeader, isLeader ? SystemLeaderRoles.NotificationCoordinator : null);
                _workers.Publish();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Reliability coordinator cycle failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.InstanceHeartbeatSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}