using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services.Coordination;

public sealed class SystemRecoveryService
{
    private readonly SystemCoordinationStore _store;
    private readonly SystemReliabilityOptions _options;
    private readonly SystemRecoveryPolicyService _policies;
    private readonly SystemInstanceRegistryService _instances;
    private readonly ILogger<SystemRecoveryService> _log;

    public SystemRecoveryService(
        SystemCoordinationStore store,
        SystemReliabilityOptions options,
        SystemRecoveryPolicyService policies,
        SystemInstanceRegistryService instances,
        ILogger<SystemRecoveryService> log)
    {
        _store = store;
        _options = options;
        _policies = policies;
        _instances = instances;
        _log = log;
    }

    public int RunStartupRecovery()
    {
        if (!_options.Enabled)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var released = _store.ReleaseStuckNotificationClaims(now);
        if (released > 0)
        {
            _log.LogWarning("Startup recovery released {Count} stuck notification claims.", released);
        }

        return released;
    }

    public SystemRecoveryHistoryRecord TryManualRecovery(string workerId, string? reason, string correlationId)
    {
        var worker = _store.GetWorker(workerId)
            ?? throw new InvalidOperationException("Worker not found.");
        return ExecuteRecovery(worker, reason ?? "Manual administrator recovery", correlationId, manual: true);
    }

    public SystemRecoveryHistoryRecord? TryAutomaticRecovery(string workerName, string correlationId)
    {
        if (!_options.AutomaticRecoveryEnabled)
        {
            return null;
        }

        var worker = _store.ListWorkers(_instances.InstanceId)
            .FirstOrDefault(w => w.WorkerName == workerName);
        if (worker is null)
        {
            return null;
        }

        return ExecuteRecovery(worker, "Automatic infrastructure recovery", correlationId, manual: false);
    }

    public void SetRecoveryEnabled(string workerId, bool enabled)
    {
        var worker = _store.GetWorker(workerId) ?? throw new InvalidOperationException("Worker not found.");
        _store.UpsertWorker(worker with { RecoveryEnabled = enabled });
    }

    public IReadOnlyList<SystemRecoveryHistoryRecord> History(int limit = 100) =>
        _store.ListRecoveryHistory(limit);

    private SystemRecoveryHistoryRecord ExecuteRecovery(
        SystemWorkerRegistryRecord worker,
        string reason,
        string correlationId,
        bool manual)
    {
        var started = DateTimeOffset.UtcNow;
        var attempt = worker.RestartCount + 1;
        if (!manual && (!worker.RecoveryEnabled || !worker.RecoveryEligible || !_policies.IsAutomaticRecoveryAllowed(worker.WorkerName)))
        {
            var denied = CreateHistory(worker, reason, attempt, correlationId, started, SystemRecoveryResult.Denied, "Recovery denied by policy.");
            _store.AppendRecovery(denied);
            _log.LogWarning("Recovery denied for worker {WorkerName}.", worker.WorkerName);
            return denied;
        }

        if (TradingCriticalWorkers.DenyAutomaticRecovery.Contains(worker.WorkerName) && !manual)
        {
            var denied = CreateHistory(worker, reason, attempt, correlationId, started, SystemRecoveryResult.Denied, "Trading-critical worker.");
            _store.AppendRecovery(denied);
            return denied;
        }

        // Safe recovery actions only — no trading worker restart.
        var succeeded = worker.WorkerName switch
        {
            "SystemIncidentNotificationHostedService" => _store.ReleaseStuckNotificationClaims(DateTimeOffset.UtcNow) >= 0,
            MonitoredWorkerNames.SystemHealthMonitor => true,
            _ when worker.WorkerType == SystemReliabilityWorkerTypes.Infrastructure => true,
            _ => false,
        };

        var result = succeeded ? SystemRecoveryResult.Succeeded : SystemRecoveryResult.Failed;
        var history = CreateHistory(
            worker,
            reason,
            attempt,
            correlationId,
            started,
            result,
            succeeded ? null : "No safe automated action available for this worker.");
        _store.AppendRecovery(history);
        if (succeeded)
        {
            _store.UpsertWorker(worker with
            {
                Status = SystemWorkerRegistryStatus.Recovering,
                RestartCount = attempt,
            });
            _log.LogInformation(
                "Recovery completed for {WorkerName} correlation {CorrelationId}.",
                worker.WorkerName,
                correlationId);
        }

        return history;
    }

    private static SystemRecoveryHistoryRecord CreateHistory(
        SystemWorkerRegistryRecord worker,
        string reason,
        int attempt,
        string correlationId,
        DateTimeOffset started,
        SystemRecoveryResult result,
        string? failure)
    {
        return new SystemRecoveryHistoryRecord(
            Guid.NewGuid().ToString("N"),
            worker.WorkerId,
            worker.WorkerName,
            worker.InstanceId,
            null,
            reason,
            attempt,
            result,
            correlationId,
            started,
            DateTimeOffset.UtcNow,
            failure);
    }
}