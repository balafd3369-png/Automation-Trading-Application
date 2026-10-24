using Microsoft.Extensions.Configuration;
using TradeWithBeast.Services;
using Xunit;

namespace TradeWithBeast.Tests.Services;

public sealed class TradingQuantityRulesTests
{
    static TradingQuantityRulesTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InstrumentLots:Index:NIFTY"] = "75",
                ["InstrumentLots:Index:BANKNIFTY"] = "30",
            })
            .Build();
        InstrumentLotMetadata.Configure(config);
    }

    [Fact]
    public void Stock_rules_use_step_one()
    {
        var rules = TradingQuantityRules.Resolve("STOCK", "RELIANCE", "RELIANCE");
        Assert.False(rules.UsesLots);
        Assert.Equal(1, rules.StepSize);
        Assert.Equal(1, rules.DefaultQuantity);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(103)]
    public void Stock_quantity_any_positive_integer(int qty)
    {
        var v = TradingQuantityRules.Validate("STOCK", "RELIANCE", "RELIANCE", qty);
        Assert.True(v.Valid);
    }

    [Fact]
    public void Option_rules_use_lot_size()
    {
        var rules = TradingQuantityRules.Resolve("OPTION", "NIFTY", "NIFTY");
        Assert.True(rules.UsesLots);
        Assert.Equal(75, rules.LotSize);
        Assert.Equal(75, rules.DefaultQuantity);
        Assert.Equal(1, rules.DefaultLots);
    }

    [Theory]
    [InlineData(75)]
    [InlineData(150)]
    [InlineData(225)]
    public void Option_valid_multiples(int qty)
    {
        var v = TradingQuantityRules.Validate("OPTION", "NIFTY", "NIFTY", qty);
        Assert.True(v.Valid);
    }

    [Theory]
    [InlineData(76)]
    [InlineData(151)]
    public void Option_rejects_non_lot_multiples(int qty)
    {
        var v = TradingQuantityRules.Validate("OPTION", "NIFTY", "NIFTY", qty);
        Assert.False(v.Valid);
    }

    [Fact]
    public void Future_uses_same_lot_rules_as_options()
    {
        var rules = TradingQuantityRules.Resolve("FUTURE", "NIFTY", "NIFTY");
        Assert.Equal(75, rules.LotSize);
        var v = TradingQuantityRules.Validate("FUTURE", "NIFTY", "NIFTY", 150);
        Assert.True(v.Valid);
    }
}
