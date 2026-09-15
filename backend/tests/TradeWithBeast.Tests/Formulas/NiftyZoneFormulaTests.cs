using TradeWithBeast.Formulas;

namespace TradeWithBeast.Tests.Formulas;

public sealed class NiftyZoneFormulaTests
{
    private const decimal YesterdayHigh = 25220m;
    private const decimal YesterdayLow = 24980m;
    private const decimal HighTwoDaysAgo = 25340m;
    private const decimal LowTwoDaysAgo = 24910m;
    private const decimal TodayOpen = 25100m;

    [Fact]
    public void B1_is_average_of_yesterday_high_and_today_open()
    {
        var expected = (YesterdayHigh + TodayOpen) / 2m;
        Assert.Equal(expected, NiftyZoneFormula.B1(YesterdayHigh, TodayOpen));
    }

    [Fact]
    public void S1_is_average_of_yesterday_low_and_today_open()
    {
        var expected = (YesterdayLow + TodayOpen) / 2m;
        Assert.Equal(expected, NiftyZoneFormula.S1(YesterdayLow, TodayOpen));
    }

    [Fact]
    public void B2_and_S2_use_two_day_history()
    {
        Assert.Equal((HighTwoDaysAgo + YesterdayHigh) / 2m, NiftyZoneFormula.B2(HighTwoDaysAgo, YesterdayHigh));
        Assert.Equal((LowTwoDaysAgo + YesterdayLow) / 2m, NiftyZoneFormula.S2(LowTwoDaysAgo, YesterdayLow));
    }

    [Fact]
    public void S_is_average_of_yesterday_range_and_today_open()
    {
        var expected = (YesterdayHigh + YesterdayLow + TodayOpen) / 3m;
        Assert.Equal(expected, NiftyZoneFormula.S(YesterdayHigh, YesterdayLow, TodayOpen));
    }

    [Theory]
    [InlineData(25200, NiftyZoneFormula.Buying)]
    [InlineData(25160, NiftyZoneFormula.Buying)]
    [InlineData(25050, NiftyZoneFormula.RangeBound)]
    [InlineData(25040, NiftyZoneFormula.Selling)]
    [InlineData(24900, NiftyZoneFormula.Selling)]
    public void Mark_classifies_last_price(decimal last, string expectedMark)
    {
        var b1 = NiftyZoneFormula.B1(YesterdayHigh, TodayOpen);
        var s1 = NiftyZoneFormula.S1(YesterdayLow, TodayOpen);
        Assert.Equal(expectedMark, NiftyZoneFormula.Mark("RELIANCE", last, b1, s1, TodayOpen));
    }

    [Fact]
    public void Mark_returns_range_bound_for_tight_nifty_spread()
    {
        var b1 = 25110m;
        var s1 = 25050m;
        Assert.True(NiftyZoneFormula.IsTightRange("NIFTY", b1, s1, TodayOpen));
        Assert.Equal(NiftyZoneFormula.RangeBound, NiftyZoneFormula.Mark("NIFTY", 25200m, b1, s1, TodayOpen));
    }

    [Fact]
    public void RoundLine_rounds_to_two_decimals()
    {
        Assert.Equal(23336.20m, NiftyZoneFormula.RoundLine(23336.204m));
        Assert.Equal(23336.21m, NiftyZoneFormula.RoundLine(23336.205m));
    }
}
