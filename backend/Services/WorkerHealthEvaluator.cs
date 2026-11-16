using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class WorkerHealthEvaluator
{
    public static bool ExpectsProgress(
        string workerName,
        bool liveTradingEnabled,
        bool brokerConnected,
        bool marketIntradayOpen)
    {
        return workerName switch
        {
            MonitoredWorkerNames.PaperTradingAgent => true,
            MonitoredWorkerNames.MockMarketService => true,
            MonitoredWorkerNames.BrokerReconciliation => brokerConnected && marketIntradayOpen,
            MonitoredWorkerNames.LiveExecutionCoordinator => liveTradingEnabled && brokerConnected,
            MonitoredWorkerNames.SystemHealthMonitor => true,
            _ => false,
        };
    }

    public static WorkerHealthDetailSnapshot Evaluate(
        WorkerHeartbeatRegistry.WorkerHeartbeatState state,
        SystemMonitoringOptions options,
        DateTimeOffset now,
        bool progressExpected)
    {
        var workerOverride = options.ResolveWorkerOverride(state.WorkerName);
        var heartbeatTimeoutSeconds = workerOverride.HeartbeatTimeoutSeconds ?? options.EffectiveWorkerHeartbeatTimeoutSeconds;
        var progressTimeoutSeconds = workerOverride.ProgressTimeoutSeconds ?? options.WorkerProgressTimeoutSeconds;
        var heartbeatTimeout = TimeSpan.FromSeconds(Math.Max(1, heartbeatTimeoutSeconds));
        var progressTimeout = TimeSpan.FromSeconds(Math.Max(1, progressTimeoutSeconds));

        var isHeartbeatStale = now - state.LastHeartbeat > heartbeatTimeout;
        var isProgressStale = progressExpected
            && state.LastSuccessfulExecution is not null
            && now - state.LastSuccessfulExecution > progressTimeout;

        if (progressExpected
            && state.LastSuccessfulExecution is null
            && now - state.LastHeartbeat > progressTimeout
            && state.Status is WorkerRunStatus.Starting)
        {
            isProgressStale = false;
        }
        else if (progressExpected
                 && state.LastSuccessfulExecution is null
                 && state.Status is WorkerRunStatus.Running or WorkerRunStatus.Starting
                 && now - state.LastHeartbeat > progressTimeout)
        {
            isProgressStale = true;
        }

        var current = MapComponentState(state, isHeartbeatStale, isProgressStale, progressExpected);

        return new WorkerHealthDetailSnapshot(
            state.WorkerName,
            state.InstanceId,
            current,
            state.LastHeartbeat,
            state.LastSuccessfulExecution,
            state.LastFailedOperation,
            state.ConsecutiveFailureCount,
            state.TotalFailureCount,
            state.LastError,
            state.LastStateChange,
            progressExpected,
            isHeartbeatStale,
            isProgressStale);
    }

    private static ComponentHealthState MapComponentState(
        WorkerHeartbeatRegistry.WorkerHeartbeatState state,
        bool heartbeatStale,
        bool progressStale,
        bool progressExpected)
    {
        if (state.Status == WorkerRunStatus.Stopped)
        {
            return ComponentHealthState.Stopped;
        }

        if (heartbeatStale)
        {
            return ComponentHealthState.Unresponsive;
        }

        if (progressStale && progressExpected)
        {
            return ComponentHealthState.Stalled;
        }

        if (state.ConsecutiveFailureCount > 0 || state.Status == WorkerRunStatus.Degraded)
        {
            return ComponentHealthState.Degraded;
        }

        if (state.Status is WorkerRunStatus.Running or WorkerRunStatus.Starting)
        {
            return ComponentHealthState.Healthy;
        }

        return ComponentHealthState.Unknown;
    }
}