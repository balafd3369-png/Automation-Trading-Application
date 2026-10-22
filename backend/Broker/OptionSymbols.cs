using System.Globalization;

namespace TradeWithBeast.Broker;

public static class OptionSymbols
{
    public static IReadOnlyList<string> QuoteKeys(string underlying, DateTime expiry, decimal strike, string side)
    {
        var name = underlying.Trim().ToUpperInvariant();
        var right = side.Equals("PE", StringComparison.OrdinalIgnoreCase) ? "PE" : "CE";
        var yy = expiry.Year % 100;
        var monthCode = MonthCode(expiry.Month);
        var dd = expiry.Day.ToString("00", CultureInfo.InvariantCulture);
        var k = decimal.ToInt32(Math.Round(strike, 0, MidpointRounding.AwayFromZero));
        var mon = expiry.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        var exchange = name == "SENSEX" ? "BFO" : "NFO";
        var compact = $"{name}{yy}{monthCode}{dd}{k}{right}";
        var monthly = $"{name}{yy}{mon}{k}{right}";
        var weeklyNamed = $"{name}{yy}{mon}{dd}{k}{right}";
        return
        [
            $"{exchange}:{compact}",
            $"{exchange}:{monthly}",
            $"{exchange}:{weeklyNamed}",
        ];
    }

    public static DateTimeOffset ExpiryIst(string isoDate)
    {
        var day = DateTime.Parse(isoDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal).Date;
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var local = DateTime.SpecifyKind(day.AddHours(15).AddMinutes(30), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, ist.GetUtcOffset(local));
    }

    /// <summary>m.Stock/Kite weekly code: 1-9 as digit, Oct/Nov/Dec as O/N/D.</summary>
    public static string MonthCode(int month) =>
        month switch
        {
            10 => "O",
            11 => "N",
            12 => "D",
            _ => month.ToString(CultureInfo.InvariantCulture),
        };
}
