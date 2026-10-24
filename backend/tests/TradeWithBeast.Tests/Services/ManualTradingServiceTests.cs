using TradeWithBeast.Models;
using TradeWithBeast.Services;
using Xunit;

namespace TradeWithBeast.Tests.Services;

public sealed class ManualTradingServiceTests
{
    [Fact]
    public void TradeSource_wire_values_are_stable()
    {
        Assert.Equal("MANUAL", TradeSourceWire.Manual);
        Assert.Equal("STRATEGY", TradeSourceWire.Strategy);
        Assert.Equal(TradeSource.Manual, TradeSourceWire.Parse("manual"));
        Assert.Equal(TradeSource.Strategy, TradeSourceWire.Parse(null));
    }
}
