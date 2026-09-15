using TradeWithBeast.Broker;

namespace TradeWithBeast.Models;

public sealed class LiveExecutionOptions
{
    public const string Section = "LiveTrading";
    public bool Enabled { get; set; }
    public int ArmMinutes { get; set; } = 30;
    public int OrderTimeoutSeconds { get; set; } = 12;
    public int ReconcileSeconds { get; set; } = 3;
    public decimal PriceProtectionPercent { get; set; } = 0.5m;
    public bool TreatMarketAsOpen { get; set; }
}

public sealed record ArmLiveRequest(string Acknowledgement);
public sealed record ConfirmLiveTradeRequest(string ProposalId, string Product = "MIS");
public sealed record LiveTradeActionRequest(string TradeId);
public sealed record LiveEodActionRequest(string TradeId, string Action);

public sealed record LiveOrderSnapshot(
    string Id,
    string TradeId,
    string LegId,
    string BrokerOrderId,
    string Exchange,
    string Symbol,
    string TransactionType,
    string Product,
    string Purpose,
    string Status,
    int Quantity,
    int FilledQuantity,
    decimal AveragePrice,
    string BrokerMessage,
    string Tag,
    DateTimeOffset UpdatedAt);

public sealed record LiveTradeSnapshot(
    string Id,
    string Strategy,
    string Underlying,
    string Product,
    string Status,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    IReadOnlyList<LiveOrderSnapshot> Orders);

public sealed record LiveExecutionEvent(
    string Id,
    string TradeId,
    string Type,
    string Message,
    DateTimeOffset At);

public sealed record LiveEodPrompt(
    string TradeId,
    string Underlying,
    string Product,
    DateTimeOffset ExpiresAt,
    string Message);

public sealed record MarketSessionSnapshot(
    string Phase,
    bool IsOpen,
    string Message,
    DateTimeOffset? NextEventAt);

public sealed record LiveExecutionSnapshot(
    bool FeatureEnabled,
    bool BrokerConnected,
    MarketSessionSnapshot MarketSession,
    bool Armed,
    DateTimeOffset? ArmedUntil,
    string State,
    PaperTradeProposal? PendingProposal,
    IReadOnlyList<LiveTradeSnapshot> Trades,
    IReadOnlyList<BrokerPosition> BrokerPositions,
    IReadOnlyList<LiveExecutionEvent> Events,
    IReadOnlyList<LiveEodPrompt> EodPrompts,
    string ReconciliationStatus,
    DateTimeOffset UpdatedAt);
