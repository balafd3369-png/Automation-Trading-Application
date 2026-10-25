namespace TradeWithBeast.Configuration;

/// <summary>Platform trading defaults (paper agent seed config and documentation for risk limits).</summary>
public sealed class TradingOptions
{
    public const string Section = "Trading";

    public decimal DefaultAllocatedCapital { get; set; } = 1_000_000m;
    public decimal DefaultProfitTarget { get; set; } = 10_000m;
    public decimal DefaultMaxDailyLoss { get; set; } = 5_000m;
    public int DefaultMaxOpenPositions { get; set; } = 30;
    public int MaxTradesPerDay { get; set; } = 0;
    public bool DefaultAutoAdmitTrades { get; set; } = true;
}
