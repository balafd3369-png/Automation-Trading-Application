namespace TradeWithBeast.Models;

public sealed record StrategyTrackRecordRow(
    string StrategyId,
    string StrategyName,
    int Trades,
    int ClosedTrades,
    int OpenTrades,
    int WinningTrades,
    int LosingTrades,
    decimal WinRatePercent,
    decimal RealizedPnl,
    decimal TotalMaxRisk,
    decimal TotalMaxReward,
    decimal PeakLiveCapital,
    decimal AvgLiveCapital);

public sealed record StrategyTrackRecordResponse(
    string MonthLabel,
    DateTimeOffset From,
    DateTimeOffset To,
    StrategyTrackRecordRow Overall,
    IReadOnlyList<StrategyTrackRecordRow> ByStrategy,
    IReadOnlyList<PaperTradeHistorySnapshot> Trades);
