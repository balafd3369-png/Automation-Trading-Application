using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class PaperTradeEconomicsTests
{
    [Fact]
    public void Capital_is_qty_times_entry()
    {
        Assert.Equal(9_800m, PaperTradeEconomics.CapitalDeployed(28, 350m));
    }

    [Fact]
    public void Long_and_short_pnl_follow_direction()
    {
        Assert.Equal(200m, PaperTradeEconomics.RealizedPnl("LONG", 100m, 110m, 20));
        Assert.Equal(-200m, PaperTradeEconomics.RealizedPnl("SHORT", 100m, 110m, 20));
        Assert.Equal(200m, PaperTradeEconomics.RealizedPnl("BEARISH", 100m, 90m, 20));
    }

    [Fact]
    public void Normalize_asserts_capital_and_pnl_for_single_leg_trade()
    {
        var legs = new List<PaperTradeHistoryLegSnapshot>
        {
            new(
                "l1", "INFY", "STOCK", "BUY", 28, 3500m, 3500m, 3600m, 3600m, null, null, null, null),
        };
        var trade = new PaperTradeHistorySnapshot(
            "t1", "CLOSED", "TEST", "INFY", "LONG", 0, 0, 0, 0, 98_000m, 2_800m, "target", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            0, 0, null, legs);

        var normalized = PaperTradeEconomics.Normalize(trade, 100_000m);

        Assert.Equal(28, normalized.Quantity);
        Assert.Equal(98_000m, normalized.CapitalRequired);
        Assert.Equal(2_800m, normalized.Pnl);
        Assert.True(Math.Abs(normalized.CapitalRequired - normalized.Quantity * normalized.EntryPrice)
            < PaperTradeEconomics.AssertionTolerance);
    }
}
