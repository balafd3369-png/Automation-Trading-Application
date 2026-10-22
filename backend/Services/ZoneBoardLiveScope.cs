namespace TradeWithBeast.Services;

internal static class ZoneBoardLiveScope
{
    public static readonly string[] Indices = ["NIFTY", "BANKNIFTY", "FINNIFTY", "SENSEX"];

    public static bool IsZoneIndex(string underlying) =>
        Indices.Any(index => index.Equals(underlying, StringComparison.OrdinalIgnoreCase));

    public static bool IsZoneCsvStrategy(string strategy) =>
        strategy is ZoneBoardPattern.B1BreakLong
            or ZoneBoardPattern.S1BreakShort
            or ZoneBoardPattern.S2BounceLong;

    public static bool Qualifies(string strategy, string underlying) =>
        IsZoneIndex(underlying) && IsZoneCsvStrategy(strategy);
}
