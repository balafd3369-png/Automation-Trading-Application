namespace TradeWithBeast.Models;

/// <summary>
/// Platform-wide feature flags. Additive only — defaults preserve legacy behavior.
/// </summary>
public sealed class TradingPlatformOptions
{
    public const string Section = "TradingPlatform";

    /// <summary>
    /// When false (default), live broker routing rejects FUTURE legs. Paper and signals unchanged.
    /// </summary>
    public bool FuturesEnabled { get; set; }
}
