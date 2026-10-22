using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class ZoneRejectionPatternTests
{
    [Fact]
    public void Backtest_counts_b1_rejection_that_reaches_s2()
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var day = new DateTimeOffset(2026, 9, 29, 9, 15, 0, ist.GetUtcOffset(new DateTime(2026, 9, 29)));
        var b1 = 326.45m;
        var s2 = 320.00m;
        var bars = new List<ChartBar>();
        for (var i = 0; i < 6; i++)
        {
            bars.Add(Bar(day.AddMinutes(i * 5), 324m, 324.5m, 323.5m, 324m));
        }

        bars.Add(Bar(day.AddMinutes(30), 325.5m, 326.6m, 325m, 325.8m));
        bars.Add(Bar(day.AddMinutes(35), 325.8m, 326.5m, 324.8m, 324.9m));
        for (var i = 8; i < 14; i++)
        {
            var close = 324.5m - i * 0.6m;
            bars.Add(Bar(day.AddMinutes(i * 5), close + 0.4m, close + 0.6m, close - 0.2m, close));
        }

        bars.Add(Bar(day.AddMinutes(70), 319.5m, 320.2m, 319.2m, 319.8m));

        var result = ZoneRejectionPattern.BacktestB1RejectionToS2("FEDERALBNK", bars, b1, s2);

        Assert.True(result.Samples >= 1);
        Assert.True(result.WinRatePercent >= 50m);
    }

    private static ChartBar Bar(DateTimeOffset istTime, decimal open, decimal high, decimal low, decimal close) =>
        new(istTime.ToUnixTimeSeconds(), open, high, low, close);
}
