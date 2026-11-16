using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services.Coordination;

public sealed class SystemWorkerRegistryPublisher
{
    private readonly SystemCoordinationStore _store;
    private readonly WorkerHeartbeatRegistry _heartbeats;
    private readonly SystemRecoveryPolicyService _policies;

    public SystemWorkerRegistryPublisher(
        SystemCoordinationStore store,
        WorkerHeartbeatRegistry heartbeats,
        SystemRecoveryPolicyService policies)
    {
        _store = store;
        _heartbeats = heartbeats;
        _policies = policies;
    }

    public void Publish()
    {
        foreach (var state in _heartbeats.States)
        {
            var workerId = $"{state.InstanceId}:{state.WorkerName}";
            var workerType = Classify(state.WorkerName);
            var eligible = _policies.IsAutomaticRecoveryAllowed(state.WorkerName);
            var status = MapStatus(state);
            _store.UpsertWorker(new SystemWorkerRegistryRecord(
                workerId,
                state.WorkerName,
                state.InstanceId,
                workerType,
                status,
                state.LastStateChange ?? state.LastHeartbeat,
                state.LastHeartbeat,
                state.LastSuccessfulExecution,
                state.LastError is not null ? state.LastHeartbeat : null,
                0,
                null,
                state.LastError,
                true,
                eligible));
        }
    }

    private static string Classify(string workerName) =>
        TradingCriticalWorkers.DenyAutomaticRecovery.Contains(workerName)
            ? SystemReliabilityWorkerTypes.Trading
            : workerName.Contains("Notification", StringComparison.OrdinalIgnoreCase)
                ? SystemReliabilityWorkerTypes.Notification
                : SystemReliabilityWorkerTypes.Infrastructure;

    private static SystemWorkerRegistryStatus MapStatus(WorkerHeartbeatRegistry.WorkerHeartbeatState state)
    {
        return state.Status switch
        {
            WorkerRunStatus.Starting => SystemWorkerRegistryStatus.Starting,
            WorkerRunStatus.Running => SystemWorkerRegistryStatus.Running,
            WorkerRunStatus.Degraded => SystemWorkerRegistryStatus.Degraded,
            WorkerRunStatus.Stopped => SystemWorkerRegistryStatus.Stopped,
            WorkerRunStatus.Stuck => SystemWorkerRegistryStatus.Unresponsive,
            _ => SystemWorkerRegistryStatus.Running,
        };
    }
}