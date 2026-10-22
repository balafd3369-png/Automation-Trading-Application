namespace TradeWithBeast.Models;

public sealed record TradeHistoryItem(
    string Id,
    string UserId,
    string Status,
    string InstrumentType,
    string Symbol,
    string Direction,
    int Quantity,
    string TriggerReason,
    decimal ProposedPrice,
    decimal? EntryPrice,
    decimal? ExitPrice,
    decimal StopLoss,
    decimal Target,
    decimal? Pnl,
    string? ExitReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? ClosedAt);

public sealed record TradeHistorySummary(
    int ClosedTrades,
    int WinningTrades,
    int LosingTrades,
    int OpenTrades,
    decimal RealizedPnl,
    int TargetHits,
    int StopLossHits,
    int TrailingStopHits,
    int BreakevenTrades);

public sealed record TradeHistoryResponse(
    string UserId,
    string Range,
    DateTimeOffset From,
    DateTimeOffset To,
    TradeHistorySummary Summary,
    IReadOnlyList<TradeHistoryItem> Trades);
