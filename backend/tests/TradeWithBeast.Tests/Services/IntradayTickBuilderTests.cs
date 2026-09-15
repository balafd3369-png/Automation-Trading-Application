using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class IntradayTickBuilderTests
{
    [Fact]
    public void BuildBars_groups_ticks_into_interval_buckets()
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var session = ChartSessionMerger.IstToday().AddHours(10);
        var ticks = new List<IntradayTickBuilder.Tick>
        {
            new(new DateTimeOffset(session.AddMinutes(0), ist.GetUtcOffset(session.AddMinutes(0))), 23576m),
            new(new DateTimeOffset(session.AddMinutes(2), ist.GetUtcOffset(session.AddMinutes(2))), 23570m),
            new(new DateTimeOffset(session.AddMinutes(6), ist.GetUtcOffset(session.AddMinutes(6))), 23560m),
            new(new DateTimeOffset(session.AddMinutes(8), ist.GetUtcOffset(session.AddMinutes(8))), 23555m),
        };

        var bars = IntradayTickBuilder.BuildBars(ticks, "5minute", 23580m);
        Assert.Equal(2, bars.Count);
        Assert.Equal(23580m, bars[0].Open);
        Assert.Equal(23570m, bars[0].Close);
        Assert.Equal(23560m, bars[1].Open);
        Assert.Equal(23555m, bars[1].Close);
    }

    [Fact]
    public void BootstrapFromOpen_creates_session_path()
    {
        var ticks = new List<IntradayTickBuilder.Tick>();
        IntradayTickBuilder.BootstrapFromOpen(ticks, 23576m, 23320m);
        Assert.True(ticks.Count > 10);
        Assert.Equal(23576m, ticks[0].Last);
        Assert.Equal(23320m, ticks[^1].Last);
    }
}
