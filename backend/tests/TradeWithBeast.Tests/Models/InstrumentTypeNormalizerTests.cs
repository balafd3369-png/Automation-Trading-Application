using TradeWithBeast.Models;

namespace TradeWithBeast.Tests.Models;

public sealed class InstrumentTypeNormalizerTests
{
    [Theory]
    [InlineData(null, InstrumentKind.Option)]
    [InlineData("", InstrumentKind.Option)]
    [InlineData("OPTION", InstrumentKind.Option)]
    [InlineData("option", InstrumentKind.Option)]
    [InlineData("FUTURE", InstrumentKind.Future)]
    [InlineData("STOCK", InstrumentKind.Stock)]
    [InlineData("INDEX", InstrumentKind.Index)]
    [InlineData("MULTI_LEG", InstrumentKind.MultiLeg)]
    [InlineData("legacy-unknown", InstrumentKind.Option)]
    public void Parse_defaults_unknown_to_option(string? raw, InstrumentKind expected) =>
        Assert.Equal(expected, InstrumentTypeNormalizer.Parse(raw));

    [Fact]
    public void Default_api_value_is_option() =>
        Assert.Equal("OPTION", InstrumentTypeNormalizer.DefaultApiValue());
}
