namespace TradeWithBeast.Models;

public sealed record StrategyLiveRecommendation(
    string Id,
    string StrategyId,
    string StrategyName,
    string Symbol,
    string Direction,
    string Headline,
    string Status,
    decimal EntryPrice,
    decimal StopLoss,
    decimal Target,
    decimal RiskPoints,
    decimal RewardPoints,
    decimal MaxRiskInr,
    decimal MaxRewardInr,
    decimal RewardRisk,
    IReadOnlyList<string> WhyNow,
    IReadOnlyList<string> IfTargetHit,
    IReadOnlyList<string> IfStopHit,
    long SignalBarTime,
    DateTimeOffset DetectedAt,
    bool CanPaperTrade);

public sealed record StrategyMonthlyStat(
    string StrategyId,
    string StrategyName,
    int TradesThisMonth,
    int ClosedTrades,
    int WinningTrades,
    decimal RealizedPnl,
    decimal PeakLiveCapital,
    decimal WinRatePercent);

public sealed record StrategyFeedResponse(
    IReadOnlyList<StrategyLiveRecommendation> LiveNow,
    IReadOnlyList<StrategyCatalogEntry> Strategies,
    IReadOnlyList<StrategyMonthlyStat> MonthlyStats,
    DateTimeOffset UpdatedAt);

public sealed record TakeStrategyPaperTradeRequest(string Strategy, string Underlying);
