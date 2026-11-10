namespace TradeWithBeast.Models;

public sealed record BoardResponse(
    BoardCounts Counts,
    IReadOnlyList<SymbolSnapshot> Symbols,
    DateTimeOffset UpdatedAt,
    string Feed);

public sealed record BoardCounts(int Buying, int Selling, int RangeBound);

public sealed record SymbolSearchHit(string Name, string CompanyName, string Exchange);

public sealed record SymbolSnapshot(
    string Name,
    decimal Last,
    decimal TodayOpen,
    decimal YesterdayHigh,
    decimal YesterdayLow,
    decimal HighTwoDaysAgo,
    decimal LowTwoDaysAgo,
    decimal B2,
    decimal B1,
    decimal S,
    decimal S1,
    decimal S2,
    string Mark,
    bool TightRange,
    decimal PreviousClose,
    string Exchange = "NSE",
    int? InstrumentToken = null,
    decimal? Bid = null,
    decimal? Ask = null,
    long? Volume = null,
    long? OpenInterest = null,
    DateTimeOffset? DataTimestamp = null,
    bool QuoteVerified = true,
    QuoteSourceQuality QuoteQuality = QuoteSourceQuality.SeedData,
    DateTimeOffset? QuoteTimestamp = null);

public sealed record ChartBar(long Time, decimal Open, decimal High, decimal Low, decimal Close);

public sealed record ChartResponse(
    string Name,
    IReadOnlyList<ChartBar> Candles,
    decimal Last,
    decimal B2,
    decimal B1,
    decimal S,
    decimal S1,
    decimal S2);

public sealed record OptionChartResponse(
    string Name,
    string TradingSymbol,
    IReadOnlyList<ChartBar> Candles,
    decimal Last,
    decimal TodayOpen,
    decimal PreviousClose,
    decimal B2,
    decimal B1,
    decimal S,
    decimal S1,
    decimal S2);

public sealed record OptionChainItem(
    decimal Strike,
    string Side,
    string TradingSymbol,
    decimal Last,
    decimal Open,
    decimal PreviousClose,
    decimal ChangePercent,
    decimal? Bid = null,
    decimal? Ask = null,
    long? Volume = null,
    long? OpenInterest = null,
    DateTimeOffset? DataTimestamp = null);

public sealed record OpenVsZonesRow(
    string Name,
    bool IsIndex,
    decimal TodayOpen,
    decimal Last,
    decimal B2,
    decimal B1,
    decimal S,
    decimal S1,
    decimal S2,
    string Mark,
    bool TightRange,
    bool OpenAboveB2,
    bool OpenAboveB1,
    bool OpenBelowS1,
    bool OpenBelowS2,
    string OpenBand,
    string OpenBandKey,
    string LtpBand,
    string LtpBandKey,
    string SessionMove);

public sealed record OpenVsZonesBoardResponse(
    DateTimeOffset UpdatedAt,
    string Feed,
    IReadOnlyList<OpenVsZonesRow> Indices,
    IReadOnlyList<OpenVsZonesRow> Stocks);

public sealed record ZoneStrategyPlaybookCard(
    string PatternId,
    string Title,
    string Direction,
    string When,
    string Target,
    string Stop);

public sealed record ZoneStockAnalyzerRow(
    string Name,
    bool IsIndex,
    string Action,
    string PatternId,
    string Headline,
    string Detail,
    string Confidence,
    int QualityScore,
    string Grade,
    string RiskClass,
    string EntryTiming,
    decimal TriggerDistancePercent,
    IReadOnlyList<string> WhyBullets,
    string MtfIntraday,
    string MtfDaily,
    string MtfWeekly,
    int MtfAlignmentScore,
    decimal TodayOpen,
    decimal Last,
    string OpenBand,
    string LtpBand,
    string SessionMove,
    string Mark,
    bool TightRange,
    decimal B1,
    decimal B2,
    decimal S1,
    decimal S2,
    decimal S,
    decimal TargetPrice,
    decimal StopPrice);

public sealed record ZoneAnalyzerTopOpportunity(
    string Name,
    string Action,
    string PatternId,
    int QualityScore,
    string Grade,
    string RiskClass);

public sealed record ZoneStockAnalyzerResponse(
    DateTimeOffset AnalyzedAt,
    string Feed,
    BoardCounts Counts,
    string BreadthNote,
    string MarketRegime,
    IReadOnlyList<ZoneAnalyzerTopOpportunity> TopOpportunities,
    IReadOnlyList<ZoneStrategyPlaybookCard> Playbook,
    IReadOnlyList<ZoneStockAnalyzerRow> Buy,
    IReadOnlyList<ZoneStockAnalyzerRow> Sell,
    IReadOnlyList<ZoneStockAnalyzerRow> Watch);

public sealed record ReversalPlaybook(
    string Title,
    IReadOnlyList<string> Stages,
    string IntradayPatternId,
    string ContinuationPatternId);

public sealed record ReversalClusterStats(
    int StocksScanned,
    int FailedMorningCount,
    int ActiveFadeCount,
    int GapContinuationCount,
    int EarlyWarningCount,
    int GapDownOpens,
    int GapUpOpens,
    int BoardBuying,
    int BoardSelling,
    string BreadthNote);

public sealed record ReversalScanRow(
    string Name,
    string PatternId,
    string Stage,
    int Score,
    string Detail,
    decimal GapVsPrevClosePercent,
    string OpenBand,
    string LtpBand,
    string SessionMove,
    string Mark,
    decimal TodayOpen,
    decimal Last,
    decimal PreviousClose,
    decimal B1,
    decimal S1,
    decimal S2);

public sealed record GapAnalyzerFeaturedClue(
    string ClueId,
    string Title,
    string PriorRule,
    string TradeRule,
    decimal BacktestWinRatePercent,
    int BacktestWins,
    int BacktestLosses,
    int TotalGapEvents,
    int SymbolsWithEvents,
    bool Qualifies,
    IReadOnlyList<string> Notes);

public sealed record GapAnalyzerPlaybookRow(
    string ClueId,
    string CatalogId,
    string Title,
    string PriorRule,
    string GapRule,
    string TradeRule,
    string Action,
    bool PaperAuto,
    decimal BacktestWinRatePercent,
    int BacktestWins,
    int BacktestLosses,
    int TotalGapEvents,
    decimal AvgContinuationRatePercent,
    int SymbolsWithEvents,
    bool Qualifies);

public sealed record GapAnalyzerTradeRow(
    string Name,
    string Action,
    string ClueId,
    string Headline,
    decimal GapVsPrevClosePercent,
    decimal BacktestWinRatePercent,
    int BacktestWins,
    int BacktestLosses,
    int BacktestEvents,
    int QualityScore,
    string Grade,
    bool LiveActionable,
    decimal TodayOpen,
    decimal Last,
    decimal PreviousClose,
    decimal B1,
    decimal S1,
    decimal S2,
    decimal TargetPrice,
    decimal StopPrice,
    string OpenBand,
    string LtpBand,
    string SessionMove,
    string Mark,
    IReadOnlyList<string> WhyBullets,
    string EntryHint);

public sealed record GapAnalyzerResponse(
    DateTimeOffset AnalyzedAt,
    string Feed,
    GapAnalyzerFeaturedClue Featured,
    IReadOnlyList<GapAnalyzerPlaybookRow> Playbooks,
    int LookbackDays,
    string BenchmarkNote,
    IReadOnlyList<GapAnalyzerTradeRow> Trades,
    int LiveActionableCount);

public sealed record ReversalScanResponse(
    DateTimeOffset AnalyzedAt,
    string Feed,
    ReversalPlaybook Playbook,
    ReversalClusterStats Stats,
    IReadOnlyList<ReversalScanRow> ActiveFades,
    IReadOnlyList<ReversalScanRow> GapContinuations,
    IReadOnlyList<ReversalScanRow> EarlyWarnings,
    IReadOnlyList<ReversalScanRow> FailedMorningOpens);

public sealed record ZoneAnalyzerPatternDetailResponse(
    string Symbol,
    string PatternId,
    int LookbackDays,
    decimal WinRatePercent,
    int Samples,
    int Wins,
    int Losses,
    decimal AvgRewardPoints,
    decimal AvgRiskPoints,
    decimal? RewardRiskRatio,
    int Last20Wins,
    int Last20Losses,
    string ConfidenceLabel,
    bool QualifiesBacktest,
    int AdaptiveSignalsLogged,
    decimal? AdaptiveWinRatePercent);
