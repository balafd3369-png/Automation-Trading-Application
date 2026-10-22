using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class ChartSessionMergerTests
{
    [Fact]
    public void LastBarIsToday_false_for_old_unix_bar()
    {
        var old = new DateTimeOffset(2026, 9, 11, 15, 15, 0, ChartSessionMerger.ParseIst("2026-09-11 15:15:00").Offset);
        var bars = new List<ChartBar> { new(old.ToUnixTimeSeconds(), 1, 2, 0.5m, 1.5m) };
        Assert.False(ChartSessionMerger.LastBarIsToday(bars));
    }

    [Fact]
    public void MinExpectedTodayBars_counts_5m_buckets_from_session_open()
    {
        var session = ChartSessionMerger.ParseIst($"{ChartSessionMerger.IstToday():yyyy-MM-dd} 11:10:00");
        var expected = ChartSessionMerger.MinExpectedTodayBars(session, "5minute");
        Assert.Equal(24, expected);
    }

    [Fact]
    public void MergeHistoricalWithToday_replaces_stale_tail()
    {
        var old = ChartSessionMerger.ParseIst("2026-09-11 15:15:00").ToUnixTimeSeconds();
        var today = ChartSessionMerger.ParseIst($"{ChartSessionMerger.IstToday():yyyy-MM-dd} 09:15:00").ToUnixTimeSeconds();
        var historical = new List<ChartBar> { new(old, 100, 110, 90, 105) };
        var todayBars = new List<ChartBar> { new(today, 23576.15m, 23590m, 23300m, 23320m) };
        var merged = ChartSessionMerger.MergeHistoricalWithToday(historical, todayBars);
        Assert.Equal(2, merged.Count);
        Assert.Equal(today, merged[^1].Time);
        Assert.Equal(old, merged[0].Time);
    }

    [Fact]
    public void BuildCurrentBar_uses_live_last()
    {
        var snap = new SymbolSnapshot(
            "NIFTY", 23320m, 23576.15m, 23600m, 23200m, 23700m, 23100m,
            23650m, 23588.08m, 23418.48m, 23388.08m, 23150m, "SELLING", false, 23576.15m);
        var bars = ChartSessionMerger.BuildCurrentBar(snap, "15minute", 23320m);
        if (ChartSessionMerger.IsCashSessionOpen(ChartSessionMerger.IstNow()))
        {
            Assert.NotEmpty(bars);
            Assert.Equal(23320m, bars[^1].Close);
            Assert.Equal(23576.15m, bars[^1].Open);
        }
        else
        {
            Assert.Empty(bars);
        }
    }
}
