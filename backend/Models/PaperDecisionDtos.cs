namespace TradeWithBeast.Models;

public sealed record QuoteMetadata(
    string Source,
    string Exchange,
    int? InstrumentToken,
    DateTimeOffset DataTimestamp,
    int AgeSeconds,
    bool IsFresh,
    decimal? Bid,
    decimal? Ask,
    long? Volume,
    long? OpenInterest,
    IReadOnlyList<string> MissingFields);

public sealed record MarketEvidence(
    decimal? EmaFast,
    decimal? EmaSlow,
    decimal? MomentumPercent,
    decimal? RealizedVolatilityPercent,
    string Trend,
    string Zone,
    string Regime,
    IReadOnlyList<string> Facts);

public sealed record MarketContextSnapshot(
    string Symbol,
    string InstrumentType,
    decimal Last,
    QuoteMetadata Quote,
    MarketEvidence Evidence);

public sealed record PaperTradeLegProposal(
    string Id,
    string Symbol,
    string Underlying,
    string InstrumentType,
    string Side,
    int Ratio,
    int Quantity,
    decimal IndicativePrice,
    decimal? Strike = null,
    string? OptionRight = null,
    DateTimeOffset? Expiry = null,
    QuoteMetadata? Quote = null);

public sealed record PaperPositionLegSnapshot(
    string Id,
    string Symbol,
    string Side,
    int Quantity,
    decimal EntryPrice,
    decimal CurrentPrice,
    decimal Pnl,
    decimal? Strike = null,
    string? OptionRight = null,
    DateTimeOffset? Expiry = null);

public sealed record RankedDecisionSnapshot(
    string Id,
    string Strategy,
    string Underlying,
    string Direction,
    decimal EvidenceScore,
    bool Accepted,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Rejections,
    decimal MaxRisk,
    decimal MaxReward,
    decimal RewardRisk,
    DateTimeOffset EvaluatedAt,
    string ScoreMeaning = "deterministic rule evidence, not probability");

public sealed record PaperTradeEventSnapshot(
    string Id,
    string TradeId,
    string Type,
    string Reason,
    decimal? Pnl,
    DateTimeOffset At);

public sealed record PaperPerformanceMetrics(
    int ClosedTrades,
    int WinningTrades,
    int LosingTrades,
    decimal WinRatePercent,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal GrossRiskOpen);

public sealed record MarketRegimeSnapshot(
    string Regime,
    DateTimeOffset DataTimestamp,
    bool IsFresh,
    IReadOnlyList<string> Evidence);

public sealed record PaperTradeHistoryLegSnapshot(
    string Id,
    string Symbol,
    string InstrumentType,
    string Side,
    int Quantity,
    decimal ProposedPrice,
    decimal? EntryPrice,
    decimal? CurrentPrice,
    decimal? ExitPrice,
    decimal? Pnl,
    decimal? Strike,
    string? OptionRight,
    DateTimeOffset? Expiry);

public sealed record PaperTradeHistorySnapshot(
    string Id,
    string Status,
    string Strategy,
    string Underlying,
    string Direction,
    decimal MaxRisk,
    decimal MaxReward,
    decimal? Pnl,
    string? ExitReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<PaperTradeHistoryLegSnapshot> Legs);

public sealed record PaperTradeHistoryResponse(
    string UserId,
    string Range,
    TradeHistorySummary Summary,
    IReadOnlyList<PaperTradeHistorySnapshot> Trades);
