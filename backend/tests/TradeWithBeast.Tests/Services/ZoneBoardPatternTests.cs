using TradeWithBeast.Formulas;
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
        const string symbol = "NIFTY";
        var bars = BuildB1BreakLongWinBars(symbol);
        var journal = ZoneBoardPattern.JournalB1BreakLong(symbol, bars);
        var result = ZoneBoardPattern.BacktestB1BreakLong(symbol, bars);

        Assert.NotEmpty(journal);
        Assert.True(result.Samples >= 1);
        Assert.True(result.Wins >= 1, "price path should reach B2 before stop");
        Assert.Equal(journal.Count, result.Samples);
        Assert.Equal(journal.Count(e => e.Result.Equals("WIN", StringComparison.OrdinalIgnoreCase)), result.Wins);
        Assert.Contains(journal, e => e.Result.Equals("WIN", StringComparison.OrdinalIgnoreCase));
    }

    private static List<ChartBar> BuildB1BreakLongWinBars(string symbol)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var session0 = new DateTimeOffset(2026, 9, 21, 9, 15, 0, ist.GetUtcOffset(new DateTime(2026, 9, 21)));
        var session1 = session0.AddDays(1);
        var session2 = session0.AddDays(2);
        var session3 = session0.AddDays(3);

        var history = new List<ChartBar>
        {
            Bar(session0, 24_980m, 25_340m, 24_900m, 25_100m),
            Bar(session1, 25_100m, 25_220m, 24_950m, 25_180m),
            Bar(session2, 25_100m, 25_200m, 24_900m, 25_120m),
            Bar(session3, 25_100m, 25_130m, 25_080m, 25_115m),
        };

        var zones = ZoneBoardPattern.BuildDailyZones(history);
        var breakDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(session3, ist).Date);
        Assert.True(zones.TryGetValue(breakDay, out var z), "need prior-session zones for break day");
        Assert.False(
            NiftyZoneFormula.IsTightRange(symbol, z.B1, z.S1, z.S),
            $"B1–S1 spread must be wide enough (B1={z.B1}, S1={z.S1}, S={z.S})");
        Assert.True(z.B1 < z.B2, $"B2 must be above B1 (B1={z.B1}, B2={z.B2})");

        var entry = z.B1 + 1m;
        var stop = z.S;
        var safeLow = Math.Max(stop + 15m, entry - 0.5m);
        var bars = new List<ChartBar>(history)
        {
            Bar(session3.AddMinutes(5), entry - 3m, entry - 2m, safeLow, z.B1 - 2m),
            Bar(session3.AddMinutes(10), z.B1 - 2m, z.B1 - 0.5m, safeLow, z.B1 - 1m),
            Bar(session3.AddMinutes(15), z.B1 - 1m, entry + 2m, safeLow, entry),
            Bar(session3.AddMinutes(20), entry, z.B2 + 20m, safeLow, z.B2 - 0.25m),
            Bar(session3.AddMinutes(25), z.B2 - 0.25m, z.B2 + 5m, safeLow, z.B2),
        };

        return bars;
    }

    private static ChartBar Bar(DateTimeOffset istTime, decimal open, decimal high, decimal low, decimal close) =>
        new(istTime.ToUnixTimeSeconds(), open, high, low, close);
}