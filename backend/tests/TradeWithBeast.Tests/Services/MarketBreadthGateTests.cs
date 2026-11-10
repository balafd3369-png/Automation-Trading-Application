using TradeWithBeast.Models;
using TradeWithBeast.Services;
using Xunit;

namespace TradeWithBeast.Tests.Services;

public sealed class MarketBreadthGateTests
{
    [Fact]
    public void Bullish_breadth_requires_buying_count_and_ratio()
    {
        Assert.True(MarketBreadthGate.IsBullishBreadth(new BoardCounts(8, 4, 10)));
        Assert.False(MarketBreadthGate.IsBullishBreadth(new BoardCounts(4, 2, 20)));
        Assert.False(MarketBreadthGate.IsBullishBreadth(new BoardCounts(10, 12, 5)));
    }
}
