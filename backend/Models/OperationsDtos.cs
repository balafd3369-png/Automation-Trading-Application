using TradeWithBeast.Broker;

namespace TradeWithBeast.Models;

public enum OperationsTradingMode
{
    Disarmed = 0,
    ManualLiveArmed = 1,
    ZoneAutoLiveArmed = 2,
}

public enum OperationsRiskHealth
{
    Healthy = 0,
    Watch = 1,
    RiskLimitApproaching = 2,
}

public enum OperationsBrokerHealthStatus
{
    Healthy = 0,
    Delayed = 1,
    Disconnected = 2,
}

public sealed record OperationsAccountRiskSummary(
    decimal TotalCapital,
    decimal AvailableCapital,
    decimal CapitalUsed,
    decimal? MarginAvailable,
    decimal? MarginUsed,
    decimal DailyPnl,
    decimal RealizedPnl,
    decimal UnrealizedPnl);

public sealed record OperationsExposureSummary(
    decimal TotalExposure,
    decimal StockExposure,
    decimal OptionExposure,
    decimal FutureExposure);

public sealed record OperationsPositionRiskSummary(
    int OpenPositions,
    decimal OpenRisk,
    decimal MaxRisk,
    string? LargestPositionSymbol,
    decimal LargestPositionExposure,
    string? LargestLosingPositionSymbol,
    decimal LargestLosingPnl);

public sealed record OperationsRiskLimitsSummary(
    decimal MaxDailyLoss,
    int MaxOpenPositions,
    decimal CapitalAllocation,
    decimal RemainingCapacity,
    int OpenPositions);

public sealed record OperationsRiskDashboard(
    OperationsAccountRiskSummary Account,
    OperationsExposureSummary Exposure,
    OperationsPositionRiskSummary PositionRisk,
    OperationsTradingMode TradingMode,
    string TradingModeLabel,
    OperationsRiskLimitsSummary Limits,
    OperationsRiskHealth Health,
    string HealthLabel,
    DateTimeOffset UpdatedAt);

public sealed record OperationsBrokerPortfolioSnapshot(
    bool Connected,
    string Provider,
    string SyncHealth,
    DateTimeOffset? LastSyncAt,
    decimal? AvailableFunds,
    decimal? UsedMargin,
    decimal? AvailableCash,
    IReadOnlyList<BrokerHoldingRow> Holdings,
    IReadOnlyList<BrokerPosition> OpenPositions,
    IReadOnlyList<BrokerOrder> Orders);

public sealed record BrokerHoldingRow(
    string Symbol,
    string Exchange,
    int Quantity,
    decimal AveragePrice,
    decimal LastPrice,
    decimal MarketValue,
    decimal Pnl);

public sealed record OperationsOrderRow(
    string OrderId,
    string Symbol,
    string Side,
    int Quantity,
    decimal Price,
    string OrderType,
    string Status,
    DateTimeOffset? Time,
    string Source,
    string ExecutionMode);

public sealed record OperationsOrdersBook(
    IReadOnlyList<OperationsOrderRow> Open,
    IReadOnlyList<OperationsOrderRow> Completed,
    IReadOnlyList<OperationsOrderRow> Cancelled,
    IReadOnlyList<OperationsOrderRow> Rejected);

public sealed record OperationsPositionRow(
    string Id,
    string Symbol,
    string InstrumentType,
    int Quantity,
    decimal EntryPrice,
    decimal CurrentPrice,
    decimal Pnl,
    string Source,
    string Status,
    string ExecutionMode);

public sealed record OperationsPositionsBook(
    IReadOnlyList<OperationsPositionRow> Open,
    IReadOnlyList<OperationsPositionRow> Closed);

public sealed record OperationsHoldingRow(
    string Symbol,
    int Quantity,
    decimal AveragePrice,
    decimal CurrentValue,
    decimal Pnl,
    string Source);

public sealed record OperationsBrokerHealthWidget(
    OperationsBrokerHealthStatus Status,
    string StatusLabel,
    bool Connected,
    DateTimeOffset? LastHeartbeat,
    int OpenOrdersCount,
    int FailedOrdersCount,
    int TodaysOrdersCount,
    int? LatencyMs,
    DateTimeOffset? LastSyncAt);

public sealed record OperationsJournalEntry(
    string Id,
    string TradeSource,
    string StrategyName,
    string InstrumentType,
    string Symbol,
    string Side,
    decimal? EntryPrice,
    decimal? ExitPrice,
    decimal? Pnl,
    DateTimeOffset? OpenTime,
    DateTimeOffset? CloseTime,
    string? Notes,
    string ExecutionMode,
    string Status);

public sealed record OperationsJournalQuery(
    string? Strategy = null,
    string? Symbol = null,
    string? Source = null,
    string? From = null,
    string? To = null);

public sealed record OperationsTimelineEntry(
    string Id,
    string Category,
    string Title,
    string Detail,
    DateTimeOffset At);

public sealed record OperationsTimelineQuery(
    string? Category = null,
    int Limit = 100);

public sealed record OperationsAuditEntry(
    string Id,
    string Category,
    string EventType,
    string Message,
    DateTimeOffset At);

public sealed record OperationsNotification(
    string Id,
    string Severity,
    string EventType,
    string Message,
    bool Read,
    DateTimeOffset At);

public sealed record OperationsEmergencyRequest(string Action, string Confirmation);

public sealed record OperationsEmergencyResult(bool Success, string Message, OperationsAuditEntry? Audit);

public sealed record OperationsJournalNotesRequest(string TradeId, string Notes);
