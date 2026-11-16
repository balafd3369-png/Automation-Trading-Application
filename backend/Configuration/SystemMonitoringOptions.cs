using TradeWithBeast.Models;

namespace TradeWithBeast.Configuration;

/// <summary>System health monitoring intervals and thresholds (seconds unless noted).</summary>
public sealed class SystemMonitoringOptions
{
    public const string Section = "SystemMonitoring";

    public bool Enabled { get; set; } = true;

    public int WorkerHeartbeatCheckSeconds { get; set; } = 15;

    /// <summary>Worker heartbeat stale threshold (Phase 1 alias; maps to WorkerHeartbeatTimeoutSeconds when unset).</summary>
    public int WorkerHeartbeatStaleSeconds { get; set; } = 45;

    public int WorkerHeartbeatTimeoutSeconds { get; set; }

    public int WorkerProgressTimeoutSeconds { get; set; } = 90;

    public int DatabaseCheckSeconds { get; set; } = 30;

    public int ComprehensiveCheckSeconds { get; set; } = 600;

    public string InstanceId { get; set; } = "";

    public bool PublicHealthIncludeDatabaseProbe { get; set; } = true;

    public bool BrokerMonitoringEnabled { get; set; } = true;

    public int BrokerProbeIntervalSeconds { get; set; } = 30;

    public int BrokerFailureThreshold { get; set; } = 3;

    public int BrokerTimeoutSeconds { get; set; } = 12;

    public bool MarketDataMonitoringEnabled { get; set; } = true;

    public int MarketDataStaleThresholdSeconds { get; set; } = 120;

    public int MarketDataProbeIntervalSeconds { get; set; } = 30;

    public bool SignalRMonitoringEnabled { get; set; } = true;

    public int SignalRProbeIntervalSeconds { get; set; } = 30;

    public Dictionary<string, WorkerMonitoringOverride> WorkerOverrides { get; set; } = new(StringComparer.Ordinal);

    public bool IncidentManagementEnabled { get; set; } = true;

    public int IncidentEvaluationIntervalSeconds { get; set; } = 30;

    public bool IncidentDeduplicationEnabled { get; set; } = true;

    public bool IncidentAutoResolveEnabled { get; set; } = true;

    public int IncidentMinimumOccurrenceThreshold { get; set; } = 1;

    public bool AlertEngineEnabled { get; set; } = true;

    public SystemIncidentSeverity AlertMinimumSeverity { get; set; } = SystemIncidentSeverity.Medium;

    public int AlertCooldownSeconds { get; set; } = 300;

    public bool AlertDeduplicationEnabled { get; set; } = true;

    public int MaximumAlertRetryCount { get; set; } = 3;

    public int EffectiveWorkerHeartbeatTimeoutSeconds =>
        WorkerHeartbeatTimeoutSeconds > 0 ? WorkerHeartbeatTimeoutSeconds : WorkerHeartbeatStaleSeconds;

    public int ClampIntervals()
    {
        WorkerHeartbeatCheckSeconds = Math.Clamp(WorkerHeartbeatCheckSeconds, 5, 300);
        WorkerHeartbeatStaleSeconds = Math.Clamp(WorkerHeartbeatStaleSeconds, 10, 600);
        if (WorkerHeartbeatTimeoutSeconds > 0)
        {
            WorkerHeartbeatTimeoutSeconds = Math.Clamp(WorkerHeartbeatTimeoutSeconds, 10, 600);
        }

        WorkerProgressTimeoutSeconds = Math.Clamp(WorkerProgressTimeoutSeconds, 15, 3600);
        DatabaseCheckSeconds = Math.Clamp(DatabaseCheckSeconds, 10, 300);
        ComprehensiveCheckSeconds = Math.Clamp(ComprehensiveCheckSeconds, 60, 3600);
        BrokerProbeIntervalSeconds = Math.Clamp(BrokerProbeIntervalSeconds, 10, 300);
        BrokerFailureThreshold = Math.Clamp(BrokerFailureThreshold, 1, 20);
        BrokerTimeoutSeconds = Math.Clamp(BrokerTimeoutSeconds, 3, 60);
        MarketDataStaleThresholdSeconds = Math.Clamp(MarketDataStaleThresholdSeconds, 30, 3600);
        MarketDataProbeIntervalSeconds = Math.Clamp(MarketDataProbeIntervalSeconds, 10, 300);
        SignalRProbeIntervalSeconds = Math.Clamp(SignalRProbeIntervalSeconds, 10, 300);
        IncidentEvaluationIntervalSeconds = Math.Clamp(IncidentEvaluationIntervalSeconds, 10, 300);
        IncidentMinimumOccurrenceThreshold = Math.Clamp(IncidentMinimumOccurrenceThreshold, 1, 50);
        AlertCooldownSeconds = Math.Clamp(AlertCooldownSeconds, 30, 86400);
        MaximumAlertRetryCount = Math.Clamp(MaximumAlertRetryCount, 0, 10);
        return EffectiveWorkerHeartbeatTimeoutSeconds;
    }

    public WorkerMonitoringOverride ResolveWorkerOverride(string workerName)
    {
        if (WorkerOverrides.TryGetValue(workerName, out var entry))
        {
            return entry;
        }

        return new WorkerMonitoringOverride();
    }
}

public sealed class WorkerMonitoringOverride
{
    public int? HeartbeatTimeoutSeconds { get; set; }

    public int? ProgressTimeoutSeconds { get; set; }
}