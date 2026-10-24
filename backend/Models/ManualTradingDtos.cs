namespace TradeWithBeast.Models;

public sealed record ManualTradingPlaceOrderRequest(
    string ExecutionMode,
    string InstrumentType,
    string Symbol,
    string Underlying,
    string TransactionType,
    string OrderType,
    int Quantity,
    string Product,
    decimal? LimitPrice = null,
    decimal? Strike = null,
    string? OptionRight = null,
    DateTimeOffset? Expiry = null);

public sealed record ManualTradingCancelOrderRequest(string OrderId);

public sealed record ManualTradingClosePositionRequest(
    string ExecutionMode,
    string TradeId,
    int? Quantity = null);

public sealed record ManualOrderSnapshot(
    string Id,
    string ExecutionMode,
    string TradeSource,
    string Status,
    string Symbol,
    string Underlying,
    string InstrumentType,
    string TransactionType,
    string OrderType,
    string Product,
    string Exchange,
    int Quantity,
    decimal? LimitPrice,
    int FilledQuantity,
    decimal AveragePrice,
    string? BrokerOrderId,
    string? TradeId,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ManualPositionSnapshot(
    string TradeId,
    string TradeSource,
    string Symbol,
    string Underlying,
    string InstrumentType,
    string Direction,
    int Quantity,
    decimal EntryPrice,
    decimal CurrentPrice,
    decimal Pnl,
    decimal CapitalRequired,
    string Product,
    DateTimeOffset OpenedAt);

public sealed record ManualHoldingSnapshot(
    string Symbol,
    string Exchange,
    int Quantity,
    decimal AveragePrice,
    decimal LastPrice,
    decimal MarketValue,
    decimal Pnl);

public sealed record ManualCapitalSummary(
    decimal? AllocatedCapital,
    decimal DeployedCapital,
    decimal AvailableCapital,
    decimal? BrokerAvailableFunds);

public sealed record ManualTradingSnapshot(
    string ExecutionMode,
    ManualCapitalSummary Capital,
    IReadOnlyList<ManualOrderSnapshot> Orders,
    IReadOnlyList<ManualPositionSnapshot> Positions,
    IReadOnlyList<ManualHoldingSnapshot> Holdings,
    IReadOnlyList<PaperTradeHistorySnapshot> TradeHistory,
    DateTimeOffset UpdatedAt);

public sealed record ManualCapitalValidationResult(
    bool Allowed,
    string Message,
    decimal RequiredCapital,
    decimal AvailableCapital);
