namespace TradeWithBeast.Models;

public sealed record PaperAgentConfig(
    decimal AllocatedCapital,
    decimal ProfitTarget,
    decimal MaxDailyLoss,
    int MaxOpenPositions,
    bool AutoAdmitTrades = true);

public sealed record PaperPositionSnapshot(
    string Id,
    string InstrumentType,
    string Symbol,
    string Direction,
    string TriggerReason,
    int Quantity,
    decimal EntryPrice,
    decimal CurrentPrice,
    decimal StopLoss,
    decimal Target,
    decimal Pnl,
    DateTimeOffset OpenedAt,
    string Strategy = "STOCK_ZONE_LONG",
    string Underlying = "",
    decimal MaxRisk = 0,
    decimal CapitalRequired = 0,
    IReadOnlyList<PaperPositionLegSnapshot>? Legs = null,
    DateTimeOffset? ExitDeadline = null);

public sealed record PaperAgentMessage(
    string Role,
    string Text,
    DateTimeOffset At);

public sealed record PaperTradeProposal(
    string Id,
    string InstrumentType,
    string Symbol,
    string Direction,
    int Quantity,
    decimal IndicativePrice,
    decimal StopLoss,
    decimal Target,
    string Reason,
    DateTimeOffset ExpiresAt,
    string Strategy = "STOCK_ZONE_LONG",
    string Underlying = "",
    decimal EvidenceScore = 0,
    decimal MaxRisk = 0,
    decimal MaxReward = 0,
    decimal CapitalRequired = 0,
    decimal RewardRisk = 0,
    IReadOnlyList<string>? Evidence = null,
    IReadOnlyList<PaperTradeLegProposal>? Legs = null,
    DateTimeOffset? DataTimestamp = null);

public sealed record PaperAgentSnapshot(
    bool PaperMode,
    bool Running,
    string State,
    decimal? BrokerAvailableFunds,
    decimal? BrokerUtilizedFunds,
    PaperAgentConfig? Config,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal TotalPnl,
    PaperTradeProposal? PendingTrade,
    IReadOnlyList<PaperPositionSnapshot> Positions,
    IReadOnlyList<PaperAgentMessage> Messages,
    DateTimeOffset UpdatedAt,
    MarketRegimeSnapshot? MarketRegime = null,
    IReadOnlyList<RankedDecisionSnapshot>? RecentDecisions = null,
    IReadOnlyList<PaperTradeEventSnapshot>? RecentEvents = null,
    PaperPerformanceMetrics? Performance = null,
    IReadOnlyList<MarketContextSnapshot>? MarketContext = null);

public sealed record PaperAgentCommandRequest(string Message);
