namespace TradeWithBeast.Formulas;

/// <summary>
/// Locked Nifty/stock zone lines. Do not replace with Dual-MA, RSI, or other indicators.
/// </summary>
public static class NiftyZoneFormula
{
    public const string Buying = "BUYING";
    public const string Selling = "SELLING";
    public const string RangeBound = "RANGE BOUND";

    public static decimal B1(decimal yesterdayHigh, decimal todayOpen)
        => Average(yesterdayHigh, todayOpen);

    public static decimal S1(decimal yesterdayLow, decimal todayOpen)
        => Average(yesterdayLow, todayOpen);

    public static decimal B2(decimal highTwoDaysAgo, decimal yesterdayHigh)
        => Average(highTwoDaysAgo, yesterdayHigh);

    public static decimal S2(decimal lowTwoDaysAgo, decimal yesterdayLow)
        => Average(lowTwoDaysAgo, yesterdayLow);

    public static decimal S(decimal yesterdayHigh, decimal yesterdayLow, decimal todayOpen)
        => Average(yesterdayHigh, yesterdayLow, todayOpen);

    public static decimal Average(params decimal[] values)
    {
        if (values is null || values.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        decimal sum = 0m;
        foreach (var value in values)
        {
            sum += value;
        }

        return sum / values.Length;
    }

    public static bool IsTightRange(string symbol, decimal b1, decimal s1, decimal todayOpen)
    {
        var spread = Math.Abs(b1 - s1);
        if (string.Equals(symbol, "NIFTY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(symbol, "FINNIFTY", StringComparison.OrdinalIgnoreCase))
        {
            return spread < 80m;
        }

        if (string.Equals(symbol, "SENSEX", StringComparison.OrdinalIgnoreCase))
        {
            return spread < 250m;
        }

        if (todayOpen == 0m)
        {
            return false;
        }

        return spread < todayOpen * 0.0035m;
    }

    public static string Mark(string symbol, decimal last, decimal b1, decimal s1, decimal todayOpen)
    {
        if (IsTightRange(symbol, b1, s1, todayOpen))
        {
            return RangeBound;
        }

        if (last >= b1)
        {
            return Buying;
        }

        if (last <= s1)
        {
            return Selling;
        }

        return RangeBound;
    }

    public static decimal RoundLine(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
