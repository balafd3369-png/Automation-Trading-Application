using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class ZoneBoardPatternTests
{
    [Fact]
    public void Levels_match_nifty_zone_formula()
    {
        var levels = ZoneBoardPattern.Levels(25220m, 25040m, 25340m, 24980m, 25100m);
        Assert.Equal(25280m, levels.B2);
        Assert.Equal(25160m, levels.B1);
        Assert.Equal(25120m, levels.S);
        Assert.Equal(25070m, levels.S1);
        Assert.Equal(25010m, levels.S2);
    }

    [Fact]
    public void B1_break_long_backtest_counts_win_when_price_reaches_b2()
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var day0 = new DateTimeOffset(2026, 9, 27, 9, 15, 0, ist.GetUtcOffset(new DateTime(2026, 9, 27)));
        var day1 = day0.AddDays(1);
        var day2 = day0.AddDays(2);
        var bars = new List<ChartBar>();
        bars.AddRange(DailySession(day0, 100m, 102m, 99m, 101m));
        bars.AddRange(DailySession(day1, 101m, 104m, 100m, 103m));
        bars.AddRange(B1BreakSession(day2, b1: 103.5m, b2: 105m, pivot: 102m));

        var result = ZoneBoardPattern.BacktestB1BreakLong("NIFTY", bars);
        Assert.True(result.Samples >= 1);
        Assert.True(result.Wins >= 1);
    }

    private static IEnumerable<ChartBar> DailySession(DateTimeOffset start, decimal open, decimal high, decimal low, decimal close)
    {
        yield return Bar(start, open, high, low, close);
    }

    private static IEnumerable<ChartBar> B1BreakSession(DateTimeOffset start, decimal b1, decimal b2, decimal pivot)
    {
        yield return Bar(start, b1 - 1m, b1 - 0.5m, pivot, b1 - 0.8m);
        yield return Bar(start.AddMinutes(5), b1 - 0.8m, b2, pivot, b2 - 0.2m);
        yield return Bar(start.AddMinutes(10), b2 - 0.2m, b2 + 0.1m, b2 - 0.3m, b2);
    }

    private static ChartBar Bar(DateTimeOffset istTime, decimal open, decimal high, decimal low, decimal close) =>
        new(istTime.ToUnixTimeSeconds(), open, high, low, close);
}
