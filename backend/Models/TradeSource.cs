namespace TradeWithBeast.Models;

/// <summary>
/// Origin of a trade in the shared trade store. Strategy automation uses <see cref="Strategy"/>; discretionary orders use <see cref="Manual"/>.
/// </summary>
public enum TradeSource
{
    Strategy = 0,
    Manual = 1,
}

public static class TradeSourceWire
{
    public const string Strategy = "STRATEGY";
    public const string Manual = "MANUAL";

    public static string ToWire(TradeSource source) =>
        source == TradeSource.Manual ? Manual : Strategy;

    public static TradeSource Parse(string? value) =>
        string.Equals(value, Manual, StringComparison.OrdinalIgnoreCase)
            ? TradeSource.Manual
            : TradeSource.Strategy;
}
