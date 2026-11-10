using System.Globalization;

namespace TradeWithBeast.Services;

public static class OrderCorrelation
{
    public static string NewId(DateTimeOffset? now = null)
    {
        var instant = now ?? DateTimeOffset.UtcNow;
        var ist = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
        var date = ist.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return $"TWB-ORD-{date}-{suffix}";
    }
}
