using TradeWithBeast.Broker;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class QuoteSourceResolverTests
{
    [Fact]
    public void VerifiedLive_includes_quote_timestamp_from_data_timestamp()
    {
        var tick = new DateTimeOffset(2026, 10, 24, 14, 35, 22, TimeSpan.Zero);
        var state = State(
            "NIFTY",
            last: 22517m,
            todayOpen: 22480m,
            yesterdayHigh: 22524m,
            yesterdayLow: 22476m,
            previousClose: 22510m,
            instrumentToken: 26000,
            historyLoaded: true,
            dataTimestamp: tick);

        if (!NseCashSession.IsLiveIntradayWindowNow())
        {
            return;
        }

        var display = QuoteSourceResolver.Resolve(state, brokerHasSession: true);
        Assert.Equal(QuoteSourceQuality.VerifiedLive, display.Quality);
        Assert.Equal(tick, display.QuoteTimestamp);
    }

    [Fact]
    public void Test1_broker_disconnected_uses_seed_not_placeholder_100()
    {
        var state = State(
            "RELIANCE",
            last: 2988m,
            todayOpen: 2968m,
            yesterdayHigh: 2994m,
            yesterdayLow: 2940m,
            previousClose: 2988m);

        var display = QuoteSourceResolver.Resolve(state, brokerHasSession: false);
        Assert.Equal(QuoteSourceQuality.SeedData, display.Quality);
        Assert.Equal(2988m, display.Last);
        Assert.NotEqual(100m, display.Last);
    }

    [Fact]
    public void Test2_weekend_off_session_index_uses_verified_close_not_live()
    {
        if (NseCashSession.IsLiveIntradayWindowNow())
        {
            return;
        }

        var state = State(
            "NIFTY",
            last: 22786m,
            todayOpen: 22680m,
            yesterdayHigh: 22524m,
            yesterdayLow: 22476m,
            previousClose: 22517m,
            historyLoaded: true);

        var display = QuoteSourceResolver.Resolve(state, brokerHasSession: true, indexSessionClose: 22517m);
        Assert.Equal(QuoteSourceQuality.VerifiedClose, display.Quality);
        Assert.Equal(22517m, display.Last);
        Assert.NotEqual(22786m, display.Last);
    }

    [Fact]
    public void Test3_new_stock_placeholder_uses_close_never_100()
    {
        var state = State(
            "MARUTI",
            last: 100m,
            todayOpen: 100m,
            yesterdayHigh: 12500m,
            yesterdayLow: 12380m,
            previousClose: 12450m,
            instrumentToken: 1234,
            historyLoaded: true);

        var display = QuoteSourceResolver.Resolve(state, brokerHasSession: true);
        Assert.Equal(QuoteSourceQuality.VerifiedClose, display.Quality);
        Assert.Equal(12450m, display.Last);
    }

    [Fact]
    public void Test4_bad_broker_tick_rejected_off_session()
    {
        if (NseCashSession.IsLiveIntradayWindowNow())
        {
            return;
        }

        var state = State(
            "RELIANCE",
            last: 500m,
            todayOpen: 1196m,
            yesterdayHigh: 1210m,
            yesterdayLow: 1188m,
            previousClose: 1196m,
            instrumentToken: 99,
            historyLoaded: true);

        var display = QuoteSourceResolver.Resolve(state, brokerHasSession: true);
        Assert.Equal(QuoteSourceQuality.VerifiedClose, display.Quality);
        Assert.Equal(1196m, display.Last);
    }

    [Fact]
    public void Test4_bad_broker_tick_rejected_during_session_when_deviation_extreme()
    {
        if (!NseCashSession.IsLiveIntradayWindowNow())
        {
            return;
        }

        var state = State(
            "RELIANCE",
            last: 200m,
            todayOpen: 1196m,
            yesterdayHigh: 1210m,
            yesterdayLow: 1188m,
            previousClose: 1196m,
            instrumentToken: 99,
            historyLoaded: true);

        var display = QuoteSourceResolver.Resolve(state, brokerHasSession: true);
        Assert.Equal(QuoteSourceQuality.Rejected, display.Quality);
        Assert.Equal(1196m, display.Last);
    }

    [Fact]
    public void Test5_market_closed_stock_shows_previous_close_quality()
    {
        if (NseCashSession.IsLiveIntradayWindowNow())
        {
            return;
        }

        var state = State(
            "TCS",
            last: 4130m,
            todayOpen: 4148m,
            yesterdayHigh: 4184m,
            yesterdayLow: 4112m,
            previousClose: 4130m,
            instrumentToken: 88,
            historyLoaded: true);

        var display = QuoteSourceResolver.Resolve(state, brokerHasSession: true);
        Assert.Equal(QuoteSourceQuality.VerifiedClose, display.Quality);
        Assert.Equal(4130m, display.Last);
    }

    private static LiveSymbolState State(
        string name,
        decimal last,
        decimal todayOpen,
        decimal yesterdayHigh,
        decimal yesterdayLow,
        decimal previousClose,
        int instrumentToken = 0,
        bool historyLoaded = false,
        DateTimeOffset? dataTimestamp = null) =>
        new(
            name,
            last,
            todayOpen,
            yesterdayHigh,
            yesterdayLow,
            previousClose,
            instrumentToken,
            dataTimestamp ?? DateTimeOffset.UtcNow,
            historyLoaded,
            []);
}
