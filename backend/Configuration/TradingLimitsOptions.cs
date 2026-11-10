namespace TradeWithBeast.Configuration;

/// <summary>Hard limits for live strategy / auto-live broker submission.</summary>
public sealed class TradingLimitsOptions
{
    public const string Section = "TradingLimits";

    /// <summary>Max strategy-live broker submissions per IST calendar day (0 = unlimited).</summary>
    public int MaxOrdersPerDay { get; set; }
}
