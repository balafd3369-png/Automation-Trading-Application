using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class OpenVsZonesBoardBuilderTests
{
    [Theory]
    [InlineData(100, 95, 90, 85, 80, "Above B2")]
    [InlineData(92, 95, 90, 85, 80, "Above B1")]
    [InlineData(79, 95, 90, 85, 80, "Below S2")]
    [InlineData(84, 95, 90, 85, 80, "Below S1")]
    [InlineData(87, 95, 90, 85, 80, "Between S1–B1")]
    [InlineData(82, 95, 90, 85, 80, "Between S1–S2")]
    public void ClassifyOpenBand_uses_today_open_vs_zone_lines(
        decimal open,
        decimal b2,
        decimal b1,
        decimal s1,
        decimal s2,
        string expected)
    {
        Assert.Equal(expected, OpenVsZonesBoardBuilder.ClassifyOpenBand(open, b2, b1, s1, s2));
    }
}
