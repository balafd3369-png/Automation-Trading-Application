using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class ChartSessionMergerIntervalTests
{
    [Fact]
    public void EnsureInterval_resamples_minute_spacing_to_five_minute()
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var sessionStart = new DateTimeOffset(
            DateTime.SpecifyKind(new DateTime(2026, 11, 10, 9, 15, 0), DateTimeKind.Unspecified),
            ist.GetUtcOffset(new DateTime(2026, 11, 10, 9, 15, 0)));
        var minuteBars = new List<ChartBar>();
        for (var i = 0; i < 20; i++)
        {
            var t = sessionStart.AddMinutes(i).ToUnixTimeSeconds();
            var px = 100m + i * 0.1m;
            minuteBars.Add(new ChartBar(t, px, px + 0.2m, px - 0.2m, px));
        }

        Assert.True(ChartSessionMerger.NeedsResample(minuteBars, "5minute"));
        var five = ChartSessionMerger.EnsureInterval(minuteBars, "5minute");
        Assert.True(five.Count < minuteBars.Count);
        Assert.True(five.Count >= 4);
        for (var i = 1; i < five.Count; i++)
        {
            Assert.Equal(300, five[i].Time - five[i - 1].Time);
        }
    }
}
