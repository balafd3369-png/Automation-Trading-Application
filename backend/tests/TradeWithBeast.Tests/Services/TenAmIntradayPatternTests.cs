using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class TenAmIntradayPatternTests
{
    [Fact]
    public void HighFade_uses_fifteen_minute_break_candle_stop_plus_ten_points()
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var day = new DateTime(2026, 9, 29, 0, 0, 0);
        var bars = new List<ChartBar>
        {
            Bar(ist, day, 9, 15, 22700, 22740, 22690, 22730),
            Bar(ist, day, 9, 30, 22730, 22760, 22720, 22750),
            Bar(ist, day, 9, 45, 22750, 22780, 22740, 22770),
            Bar(ist, day, 10, 0, 22770, 22790, 22760, 22785),
            Bar(ist, day, 11, 0, 22780, 22785, 22740, 22745),
            Bar(ist, day, 11, 15, 22745, 22750, 22720, 22725),
        };
        var now = new DateTimeOffset(day.AddHours(11).AddMinutes(20), ist.GetUtcOffset(day.AddHours(11).AddMinutes(20)));

        var ok = TenAmIntradayPattern.TryBuildHighFade("NIFTY", bars, 22732m, now, out var signal);

        Assert.True(ok);
        Assert.NotNull(signal);
        Assert.Equal(TenAmIntradayPattern.HighFadeStrategy, signal!.Strategy);
        Assert.Equal(22725m + 10m, signal.StopLoss);
    }

    private static ChartBar Bar(
        TimeZoneInfo ist,
        DateTime day,
        int hour,
        int minute,
        decimal open,
        decimal high,
        decimal low,
        decimal close)
    {
        var local = day.AddHours(hour).AddMinutes(minute);
        var unix = new DateTimeOffset(local, ist.GetUtcOffset(local)).ToUnixTimeSeconds();
        return new(unix, open, high, low, close);
    }
}
