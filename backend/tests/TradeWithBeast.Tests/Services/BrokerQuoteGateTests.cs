using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class BrokerQuoteGateTests
{
    [Fact]
    public void Rejects_last_price_on_non_trading_day()
    {
        if (NseCashSession.IsTradingDay(NseCashSession.IstToday()))
        {
            return;
        }

        var decision = BrokerQuoteGate.EvaluateLastPrice("NIFTY", 22786m, 22517m, 22480m, 22400m);
        Assert.False(decision.Accept);
        Assert.Equal(BrokerQuoteGate.RejectReason.NonTradingDay, decision.Reason);
    }

    [Fact]
    public void Rejects_last_price_when_cash_session_closed()
    {
        if (!NseCashSession.IsTradingDay(NseCashSession.IstToday()) || NseCashSession.IsLiveIntradayWindowNow())
        {
            return;
        }

        var decision = BrokerQuoteGate.EvaluateLastPrice("NIFTY", 22786m, 22517m, 22480m, 22400m);
        Assert.False(decision.Accept);
        Assert.True(
            decision.Reason is BrokerQuoteGate.RejectReason.OffSession or BrokerQuoteGate.RejectReason.NonTradingDay);
    }

    [Fact]
    public void FilterBrokerIntradayBars_drops_bar_with_absurd_jump_from_prior_close()
    {
        var t1 = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();
        var t2 = new DateTimeOffset(2026, 10, 23, 10, 5, 0, TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();
        var bars = new List<ChartBar>
        {
            new(t1, 22500m, 22520m, 22490m, 22510m),
            new(t2, 24800m, 24850m, 24790m, 24820m),
        };

        var clean = BrokerQuoteGate.FilterBrokerIntradayBars(bars, "5minute");
        Assert.Single(clean);
        Assert.Equal(22510m, clean[0].Close);
    }

    [Fact]
    public void ShouldApplyTodayOpen_rejects_open_far_from_anchor_when_session_live()
    {
        if (!NseCashSession.IsLiveIntradayWindowNow())
        {
            return;
        }

        var ok = BrokerQuoteGate.ShouldApplyTodayOpen("NIFTY", 22500m, 22490m, 0m, 22400m);
        var bad = BrokerQuoteGate.ShouldApplyTodayOpen("NIFTY", 24800m, 22500m, 0m, 22400m);
        Assert.True(ok);
        Assert.False(bad);
    }
}
