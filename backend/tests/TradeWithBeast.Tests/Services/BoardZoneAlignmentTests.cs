using TradeWithBeast.Formulas;
using TradeWithBeast.Models;

namespace TradeWithBeast.Tests.Services;

public sealed class BoardZoneAlignmentTests
{
    [Fact]
    public void Board_row_zones_match_NiftyZoneFormula()
    {
        AssertZones(
            "NIFTY",
            todayOpen: 22687.75m,
            yesterdayHigh: 23120.9m,
            yesterdayLow: 22649.95m,
            highTwoDaysAgo: 23175.1m,
            lowTwoDaysAgo: 22845.25m,
            b2: 23148.0m,
            b1: 22904.33m,
            s: 22819.53m,
            s1: 22668.85m,
            s2: 22747.60m);
    }

    [Theory]
    [InlineData("BANKNIFTY", 54734.45, 55866.55, 54696.45, 55883.7, 55335.05, 55875.13, 55300.50, 55099.15, 54715.45, 55015.75)]
    public void Board_row_zones_match_formula_for_indices(
        string name,
        decimal todayOpen,
        decimal yH,
        decimal yL,
        decimal h2,
        decimal l2,
        decimal b2,
        decimal b1,
        decimal s,
        decimal s1,
        decimal s2)
    {
        AssertZones(name, todayOpen, yH, yL, h2, l2, b2, b1, s, s1, s2);
    }

    private static void AssertZones(
        string name,
        decimal todayOpen,
        decimal yesterdayHigh,
        decimal yesterdayLow,
        decimal highTwoDaysAgo,
        decimal lowTwoDaysAgo,
        decimal b2,
        decimal b1,
        decimal s,
        decimal s1,
        decimal s2)
    {
        var expectedB2 = NiftyZoneFormula.RoundLine(NiftyZoneFormula.B2(highTwoDaysAgo, yesterdayHigh));
        var expectedB1 = NiftyZoneFormula.RoundLine(NiftyZoneFormula.B1(yesterdayHigh, todayOpen));
        var expectedS = NiftyZoneFormula.RoundLine(NiftyZoneFormula.S(yesterdayHigh, yesterdayLow, todayOpen));
        var expectedS1 = NiftyZoneFormula.RoundLine(NiftyZoneFormula.S1(yesterdayLow, todayOpen));
        var expectedS2 = NiftyZoneFormula.RoundLine(NiftyZoneFormula.S2(lowTwoDaysAgo, yesterdayLow));

        Assert.Equal(expectedB2, b2);
        Assert.Equal(expectedB1, b1);
        Assert.Equal(expectedS, s);
        Assert.Equal(expectedS1, s1);
        Assert.Equal(expectedS2, s2);

        _ = new SymbolSnapshot(
            name,
            last: todayOpen,
            todayOpen,
            yesterdayHigh,
            yesterdayLow,
            highTwoDaysAgo,
            lowTwoDaysAgo,
            b2,
            b1,
            s,
            s1,
            s2,
            NiftyZoneFormula.Mark(name, todayOpen, expectedB1, expectedS1, todayOpen),
            NiftyZoneFormula.IsTightRange(name, expectedB1, expectedS1, todayOpen),
            previousClose: 0m);
    }
}
