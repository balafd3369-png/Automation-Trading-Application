namespace TradeWithBeast.Models;

public enum LiveOrderSafetyPolicy
{
    NewPlacement = 0,
    Modification = 1,
    Cancellation = 2,
    RiskReductionExit = 3,
    StatusInquiry = 4,
}

public sealed record LiveTradingEmergencyGateSnapshot(
    bool IsHalted,
    string Reason,
    DateTimeOffset? ChangedUtc,
    string? ChangedBy);

public sealed record TradingSafetySummary(
    LiveTradingEmergencyGateSnapshot EmergencyGate,
    BrokerCircuitBreakerSnapshot CircuitBreaker,
    int NonTerminalManualLiveOrders,
    int NonTerminalStrategyOrders,
    int UnknownOrderCount,
    int StuckSubmittedCount,
    DateTimeOffset GeneratedUtc);

public sealed record EmergencyGateChangeRequest
{
    public string? Reason { get; set; }
    public string Confirmation { get; set; } = "";
}

public sealed record ManualOrderResolutionRequest
{
    public string Resolution { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Confirmation { get; set; } = "";
}

public sealed record CircuitBreakerResetRequest
{
    public string Reason { get; set; } = "";
    public string Confirmation { get; set; } = "";
}