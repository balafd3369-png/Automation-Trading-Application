using System.Text.Json.Serialization;

namespace TradeWithBeast.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemWorkerRegistryStatus
{
    Starting = 0,
    Running = 1,
    Degraded = 2,
    Stopped = 3,
    Unresponsive = 4,
    Recovering = 5,
    Failed = 6,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemRecoveryResult
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Skipped = 3,
    Denied = 4,
}

public sealed record SystemInstanceRecord(
    string InstanceId,
    string HostName,
    string ApplicationVersion,
    bool IsDraining,
    bool IsLeader,
    string? LeaderRole,
    DateTimeOffset RegisteredUtc,
    DateTimeOffset LastHeartbeatUtc,
    DateTimeOffset? LeaseExpiresUtc);

public sealed record DistributedLockRecord(
    string LockName,
    string OwnerInstanceId,
    string LockToken,
    long FencingToken,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    DateTimeOffset LastRenewedUtc,
    bool IsExpired);

public sealed record SystemWorkerRegistryRecord(
    string WorkerId,
    string WorkerName,
    string InstanceId,
    string WorkerType,
    SystemWorkerRegistryStatus Status,
    DateTimeOffset StartedUtc,
    DateTimeOffset LastHeartbeatUtc,
    DateTimeOffset? LastSuccessfulExecutionUtc,
    DateTimeOffset? LastFailureUtc,
    int RestartCount,
    string? CurrentLease,
    string? LastError,
    bool RecoveryEnabled,
    bool RecoveryEligible);

public sealed record LeaderElectionRecord(
    string Role,
    string LeaderInstanceId,
    long FencingToken,
    DateTimeOffset ElectedUtc,
    DateTimeOffset LeaseExpiresUtc,
    DateTimeOffset LastHeartbeatUtc);

public sealed record SystemRecoveryHistoryRecord(
    string Id,
    string WorkerId,
    string WorkerName,
    string InstanceId,
    string? TriggeringIncidentId,
    string RecoveryReason,
    int AttemptNumber,
    SystemRecoveryResult Result,
    string CorrelationId,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc,
    string? FailureReason);

public sealed record SystemRecoveryPolicyRecord(
    string WorkerName,
    bool AutomaticRecoveryAllowed,
    int MaxAttempts,
    int CooldownSeconds,
    string Notes);

public sealed record DistributedLockAcquireResult(
    bool Acquired,
    string? LockToken,
    long FencingToken,
    DateTimeOffset? ExpiresUtc,
    string? ConflictOwner);

public sealed record ManualRecoveryRequest
{
    public string? Reason { get; set; }
}

public static class SystemReliabilityWorkerTypes
{
    public const string Infrastructure = "infrastructure";
    public const string Notification = "notification";
    public const string Incident = "incident";
    public const string Trading = "trading";
}

public static class TradingCriticalWorkers
{
    public static readonly HashSet<string> DenyAutomaticRecovery = new(StringComparer.Ordinal)
    {
        MonitoredWorkerNames.PaperTradingAgent,
        MonitoredWorkerNames.LiveExecutionCoordinator,
        MonitoredWorkerNames.BrokerReconciliation,
        MonitoredWorkerNames.MockMarketService,
    };
}

public static class SystemLeaderRoles
{
    public const string IncidentEvaluation = "singleton:incident-evaluation";
    public const string ComprehensiveHealth = "singleton:comprehensive-health";
    public const string NotificationCoordinator = "singleton:notification-coordinator";
}

public static class SystemLockNames
{
    public const string NotificationDispatch = "lock:notification-dispatch";
    public const string IncidentEvaluation = "lock:incident-evaluation";
}