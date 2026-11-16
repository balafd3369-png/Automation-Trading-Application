using TradeWithBeast.Broker;

namespace TradeWithBeast.Tests.Broker;

public sealed class OptionChartLabelParserTests
{
    [Fact]
    public void TryParse_parses_nifty_pe_label()
    {
        Assert.True(OptionChartLabelParser.TryParse("NIFTY 17 NOV 25150 PE", out var spec));
        Assert.Equal("NIFTY", spec.Underlying);
        Assert.Equal(25150m, spec.Strike);
        Assert.Equal("PE", spec.Side);
        Assert.Equal(11, spec.Expiry.Month);
        Assert.Equal(17, spec.Expiry.Day);
    }

    [Fact]
    public void LooksLikeOptionChartName_rejects_index()
    {
        Assert.False(OptionChartLabelParser.LooksLikeOptionChartName("NIFTY"));
    }
}