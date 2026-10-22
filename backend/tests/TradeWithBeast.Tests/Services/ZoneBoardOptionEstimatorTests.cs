using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class ZoneBoardOptionEstimatorTests
{
    [Fact]
    public void Long_trade_recommends_ce_with_itm_and_otm_strikes()
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var entry = new DateTimeOffset(2026, 9, 10, 10, 0, 0, ist.GetUtcOffset(new DateTime(2026, 9, 10)));
        var exit = entry.AddHours(2);
        var trade = new ZoneBoardPattern.ZoneBoardTradeJournalEntry(
            "NIFTY",
            ZoneBoardPattern.B1BreakLong,
            "LONG",
            DateOnly.FromDateTime(entry.Date),
            entry,
            25100m,
            25000m,
            25200m,
            25200m,
            25100m,
            25050m,
            25020m,
            24980m,
            exit,
            25180m,
            "TARGET",
            80m,
            "WIN",
            "test");

        var est = ZoneBoardOptionEstimator.Estimate(trade);
        Assert.Equal("CE", est.OptionRight);
        Assert.True(est.Itm.Strike < est.AtmStrike);
        Assert.True(est.Otm.Strike > est.AtmStrike);
        Assert.Equal(65, est.LotSize);
    }
}
