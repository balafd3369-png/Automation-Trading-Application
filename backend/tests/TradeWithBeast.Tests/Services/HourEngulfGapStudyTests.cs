using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class HourEngulfGapStudyTests
{
    [Fact]
    public void Green_two_red_cover_detects_close_below_green_open()
    {
        var g = new HourEngulfGapStudy.HourBar(default, 10, 100, 105, 99, 104);
        var r1 = new HourEngulfGapStudy.HourBar(default, 11, 103, 106, 100, 101);
        var r2 = new HourEngulfGapStudy.HourBar(default, 12, 101, 102, 97, 98);
        Assert.True(HourEngulfGapStudy.TryGreenTwoRedCover(g, r1, r2));
    }

    [Fact]
    public void Green_two_red_rejects_when_second_red_closes_inside_green_body()
    {
        var g = new HourEngulfGapStudy.HourBar(default, 10, 100, 105, 99, 104);
        var r1 = new HourEngulfGapStudy.HourBar(default, 11, 103, 106, 100, 101);
        var r2 = new HourEngulfGapStudy.HourBar(default, 12, 101, 102, 100.5m, 102);
        Assert.False(HourEngulfGapStudy.TryGreenTwoRedCover(g, r1, r2));
    }

    [Fact]
    public void Red_two_green_cover_detects_close_above_red_open()
    {
        var r = new HourEngulfGapStudy.HourBar(default, 10, 105, 106, 100, 101);
        var g1 = new HourEngulfGapStudy.HourBar(default, 11, 99, 101, 98, 100);
        var g2 = new HourEngulfGapStudy.HourBar(default, 12, 100, 108, 99, 107);
        Assert.True(HourEngulfGapStudy.TryRedTwoGreenCover(r, g1, g2));
    }
}
