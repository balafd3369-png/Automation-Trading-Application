namespace TradeWithBeast.Configuration;

/// <summary>
/// Distributed reliability and safe recovery. SQLite coordination is safe only when all instances share one database file (same machine or shared volume).
/// Cross-machine safety requires a shared database + coordination backend — not provided by local SQLite alone.
/// </summary>
public sealed class SystemReliabilityOptions
{
    public const string Section = "SystemReliability";

    public bool Enabled { get; set; } = true;

    public bool InstanceRegistryEnabled { get; set; } = true;

    public bool DistributedLocksEnabled { get; set; } = true;

    /// <summary>When false, every instance acts as leader (Phase 1–5 default behavior).</summary>
    public bool LeaderElectionEnabled { get; set; }

    public bool AutomaticRecoveryEnabled { get; set; } = true;

    public int InstanceHeartbeatSeconds { get; set; } = 15;

    public int InstanceStaleSeconds { get; set; } = 60;

    public int LockDefaultLeaseSeconds { get; set; } = 45;

    public int LockRenewalSeconds { get; set; } = 15;

    public int LeaderLeaseSeconds { get; set; } = 30;

    public int WorkerHeartbeatPublishSeconds { get; set; } = 10;

    public int WorkerHeartbeatIntervalSeconds { get; set; } = 10;

    public int WorkerDegradedThresholdSeconds { get; set; } = 30;

    public int WorkerUnresponsiveThresholdSeconds { get; set; } = 60;

    public int WorkerFailedThresholdSeconds { get; set; } = 120;

    public int RecoveryMaxAttempts { get; set; } = 3;

    public int RecoveryCooldownSeconds { get; set; } = 120;

    public int NotificationProcessingLeaseSeconds { get; set; } = 120;

    public int SqliteBusyTimeoutMs { get; set; } = 5000;

    public bool SqliteWalMode { get; set; } = true;

    public string ApplicationVersion { get; set; } = "1.0";

    public void Clamp()
    {
        InstanceHeartbeatSeconds = Math.Clamp(InstanceHeartbeatSeconds, 5, 120);
        InstanceStaleSeconds = Math.Clamp(InstanceStaleSeconds, 20, 600);
        LockDefaultLeaseSeconds = Math.Clamp(LockDefaultLeaseSeconds, 10, 600);
        LockRenewalSeconds = Math.Clamp(LockRenewalSeconds, 5, 300);
        LeaderLeaseSeconds = Math.Clamp(LeaderLeaseSeconds, 10, 300);
        WorkerHeartbeatPublishSeconds = Math.Clamp(WorkerHeartbeatPublishSeconds, 5, 120);
        WorkerHeartbeatIntervalSeconds = Math.Clamp(WorkerHeartbeatIntervalSeconds, 5, 120);
        WorkerDegradedThresholdSeconds = Math.Clamp(WorkerDegradedThresholdSeconds, 10, 600);
        WorkerUnresponsiveThresholdSeconds = Math.Clamp(WorkerUnresponsiveThresholdSeconds, 15, 900);
        WorkerFailedThresholdSeconds = Math.Clamp(WorkerFailedThresholdSeconds, 30, 3600);
        RecoveryMaxAttempts = Math.Clamp(RecoveryMaxAttempts, 0, 10);
        RecoveryCooldownSeconds = Math.Clamp(RecoveryCooldownSeconds, 30, 86400);
        NotificationProcessingLeaseSeconds = Math.Clamp(NotificationProcessingLeaseSeconds, 30, 3600);
        SqliteBusyTimeoutMs = Math.Clamp(SqliteBusyTimeoutMs, 500, 60000);
    }
}