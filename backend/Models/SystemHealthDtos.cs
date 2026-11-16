using System.Text.Json.Serialization;

namespace TradeWithBeast.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemHealthOverallStatus
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Critical = 3,
    Maintenance = 4,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkerRunStatus
{
    Unknown = 0,
    Starting = 1,
    Running = 2,
    Degraded = 3,
    Stopped = 4,
    Stuck = 5,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComponentHealthState
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Stalled = 3,
    Unresponsive = 4,
    Stopped = 5,
}

public sealed record WorkerHeartbeatSnapshot(
    string WorkerName,
    string InstanceId,
    WorkerRunStatus Status,
    DateTimeOffset? LastHeartbeat,
    DateTimeOffset? LastSuccessfulExecution,
    string? LastError,
    int ConsecutiveFailureCount,
    bool IsStale);

public sealed record WorkerHealthDetailSnapshot(
    string WorkerName,
    string InstanceId,
    ComponentHealthState CurrentState,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastSuccessfulOperationUtc,
    DateTimeOffset? LastFailedOperationUtc,
    int ConsecutiveFailureCount,
    int TotalFailureCount,
    string? LastError,
    DateTimeOffset? LastStateChangeUtc,
    bool ProgressExpected,
    bool IsHeartbeatStale,
    bool IsProgressStale);

public sealed record DatabaseHealthSnapshot(
    bool CanConnect,
    bool CanRead,
    bool CanWrite,
    string? Error,
    DateTimeOffset CheckedAt,
    long LatencyMs);

public sealed record ApplicationRuntimeSnapshot(
    long WorkingSetBytes,
    long GcHeapBytes,
    int ThreadPoolWorkerThreads,
    int ThreadPoolCompletionPortThreads,
    int ThreadPoolPendingWorkItems,
    int UnobservedTaskExceptionCount,
    DateTimeOffset ProcessStartedAt);

public sealed record BrokerConnectionHealthItem(
    string BrokerId,
    string DisplayName,
    bool SessionPresent,
    bool CommunicationVerified,
    DateTimeOffset? LastSuccessfulCommunicationAt,
    DateTimeOffset? LastFailureAt,
    int ConsecutiveFailures,
    string? LastError,
    ComponentHealthState State);

public sealed record BrokerConnectivityHealthSnapshot(
    ComponentHealthState State,
    IReadOnlyList<BrokerConnectionHealthItem> Connections,
    DateTimeOffset? LastSuccessfulProbeAt,
    DateTimeOffset? LastFailureAt,
    int ConsecutiveFailures,
    string? LastError,
    DateTimeOffset CheckedAt,
    long ProbeDurationMs);

public sealed record MarketDataInstrumentHealth(
    string Symbol,
    DateTimeOffset? LastBoardUpdateAt,
    DateTimeOffset? LastPriceTimestamp,
    decimal? LastPrice,
    bool QuoteVerified,
    bool SubscriptionActive,
    bool UpdatesExpected,
    bool IsStale,
    long StaleDurationSeconds);

public sealed record MarketDataHealthSnapshot(
    ComponentHealthState State,
    string FeedSource,
    bool AdminBrokerConnected,
    DateTimeOffset? LastFeedUpdateAt,
    bool MarketIntradayOpen,
    bool MarketTradingDay,
    int WatchedChartSubscriptions,
    IReadOnlyList<MarketDataInstrumentHealth> Instruments,
    int ConsecutiveProbeFailures,
    string? LastError,
    DateTimeOffset CheckedAt,
    long ProbeDurationMs);

public sealed record SignalRServerHealthSnapshot(
    ComponentHealthState State,
    int ActiveConnections,
    int TotalConnectionsObserved,
    int DisconnectCount,
    int ConnectionFailureCount,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    DateTimeOffset? LastSuccessfulHubActivityAt,
    string? LastError,
    string Note,
    DateTimeOffset CheckedAt,
    long ProbeDurationMs);

public sealed record ComponentHealthProbeResult(
    string Component,
    string ProbeName,
    ComponentHealthState Status,
    DateTimeOffset Timestamp,
    long DurationMs,
    string? Error,
    string? Detail);

public sealed record SystemHealthComponentSummary(
    string Component,
    ComponentHealthState Status,
    DateTimeOffset? LastSuccessfulCheck,
    DateTimeOffset? LastFailure,
    int ConsecutiveFailures,
    string? LastError,
    DateTimeOffset CheckedAt,
    object? Metrics);

public sealed record SystemHealthLiveSnapshot(
    SystemHealthOverallStatus OverallStatus,
    string InstanceId,
    string ApiStatus,
    DatabaseHealthSnapshot Database,
    IReadOnlyList<WorkerHeartbeatSnapshot> Workers,
    ApplicationRuntimeSnapshot Runtime,
    DateTimeOffset? LastComprehensiveReportAt,
    string? LastComprehensiveReportId);

public sealed record SystemHealthReportEntry(
    string Id,
    string ReportKind,
    SystemHealthOverallStatus OverallStatus,
    SystemHealthComprehensiveReport Payload,
    DateTimeOffset CreatedAt);

public sealed record SystemHealthComprehensiveReport(
    SystemHealthOverallStatus OverallStatus,
    string InstanceId,
    DatabaseHealthSnapshot Database,
    IReadOnlyList<WorkerHeartbeatSnapshot> Workers,
    ApplicationRuntimeSnapshot Runtime,
    IReadOnlyList<string> Findings,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ComponentHealthProbeResult>? ComponentProbes = null,
    BrokerConnectivityHealthSnapshot? Broker = null,
    MarketDataHealthSnapshot? MarketData = null,
    SignalRServerHealthSnapshot? SignalR = null,
    IReadOnlyList<WorkerHealthDetailSnapshot>? WorkerDetails = null)
{
    public IReadOnlyList<ComponentHealthProbeResult> ComponentProbes { get; init; } = ComponentProbes ?? [];
    public IReadOnlyList<WorkerHealthDetailSnapshot> WorkerDetails { get; init; } = WorkerDetails ?? [];
}

public static class MonitoredWorkerNames
{
    public const string PaperTradingAgent = "PaperTradingAgent";
    public const string LiveExecutionCoordinator = "LiveExecutionCoordinator";
    public const string BrokerReconciliation = "BrokerReconciliationService";
    public const string MockMarketService = "MockMarketService";
    public const string SystemHealthMonitor = "SystemHealthMonitoring";
}

public static class SystemHealthComponents
{
    public const string Database = "database";
    public const string Runtime = "runtime";
    public const string Workers = "workers";
    public const string Broker = "broker";
    public const string MarketData = "market-data";
    public const string SignalR = "signalr";
}