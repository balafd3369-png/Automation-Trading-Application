namespace TradeWithBeast.Configuration;

public sealed class TradingExecutionSafetyOptions
{
    public const string Section = "TradingExecutionSafety";

    /// <summary>When true, unknown gate load failures block new live placement (fail-safe).</summary>
    public bool FailSafeWhenGateUnavailable { get; set; } = true;

    public bool OrderStatusPollingEnabled { get; set; } = true;

    public int OrderStatusPollingIntervalSeconds { get; set; } = 15;

    public int MaxConcurrentPolls { get; set; } = 4;

    public int StuckSubmittedWarningSeconds { get; set; } = 120;

    public int UnknownOrderCriticalSeconds { get; set; } = 300;

    public bool PersistEmergencyGateState { get; set; } = true;

    public void Clamp()
    {
        OrderStatusPollingIntervalSeconds = Math.Clamp(OrderStatusPollingIntervalSeconds, 5, 120);
        MaxConcurrentPolls = Math.Clamp(MaxConcurrentPolls, 1, 20);
        StuckSubmittedWarningSeconds = Math.Clamp(StuckSubmittedWarningSeconds, 30, 3600);
        UnknownOrderCriticalSeconds = Math.Clamp(UnknownOrderCriticalSeconds, 60, 86400);
    }
}