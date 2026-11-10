namespace TradeWithBeast.Models;

/// <summary>Stages stored on the order journey timeline (searchable by correlation id).</summary>
public static class OrderJourneyStages
{
    public const string Submitted = "Submitted";
    public const string Validated = "Validated";
    public const string RiskApproved = "RiskApproved";
    public const string RiskRejected = "RiskRejected";
    public const string CircuitBreakerBlocked = "CircuitBreakerBlocked";
    public const string BrokerSent = "BrokerSent";
    public const string BrokerAccepted = "BrokerAccepted";
    public const string BrokerRejected = "BrokerRejected";
    public const string PartiallyFilled = "PartiallyFilled";
    public const string Filled = "Filled";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";
    public const string DailyOrderLimitExceeded = "DailyOrderLimitExceeded";
    public const string StructuredRiskDecision = "StructuredRiskDecision";
}

public sealed record OrderJourneyEvent(
    string Stage,
    string? Detail,
    string? Reason,
    DateTimeOffset At);

public sealed record OrderJourneySummary(
    string CorrelationId,
    string UserId,
    string Source,
    string ExecutionMode,
    string? StrategyId,
    string? Symbol,
    string? OrderId,
    string CurrentStage,
    string? LastReason,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderJourneyEvent> Timeline);

public sealed record OrderJourneyLookup(
    string CorrelationId,
    string Status,
    string? Reason,
    DateTimeOffset Time,
    string? User,
    string? Symbol,
    string? Source,
    string? ExecutionMode,
    IReadOnlyList<OrderJourneyEvent> Timeline);

public sealed record TesterOperationsDashboard(
    OperationsBrokerHealthWidget Broker,
    BrokerCircuitBreakerSnapshot CircuitBreaker,
    IReadOnlyList<OrderJourneySummary> RecentRejections,
    IReadOnlyList<OrderJourneySummary> RecentOrders,
    IReadOnlyList<LiveExecutionEvent> RecentLiveEvents,
    DateTimeOffset GeneratedAt);

public sealed record BrokerCircuitBreakerSnapshot(
    bool LiveOrdersBlocked,
    string State,
    int ConsecutiveFailures,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    DateTimeOffset? TrippedAt,
    string? TripReason,
    DateTimeOffset? LastHeartbeatAt);
