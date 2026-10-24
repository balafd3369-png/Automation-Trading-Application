using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class BoardQuoteVerificationTests
{
    [Fact]
    public void IsDefaultDemoSeed_detects_placeholder_equity_seed()
    {
        var seed = new BoardQuoteVerification.LiveSymbolFields(
            "ICICIPRULI", 100m, 100m, 108m, 92m, 0m, 0, null);
        Assert.True(BoardQuoteVerification.IsDefaultDemoSeed(seed));
    }

    [Fact]
    public void IsCatalogPlaceholderPrice_detects_100_ltp_after_real_zone_history()
    {
        var maruti = new BoardQuoteVerification.LiveSymbolFields(
            "MARUTI", 100m, 100m, 12500m, 12380m, 12450m, 12345, DateTimeOffset.UtcNow);
        Assert.True(BoardQuoteVerification.IsCatalogPlaceholderPrice(maruti));
    }

    [Fact]
    public void HasVerifiedLiveQuote_requires_token_timestamp_and_non_seed_when_broker_on()
    {
        var seed = new BoardQuoteVerification.LiveSymbolFields(
            "ICICIPRULI", 100m, 100m, 108m, 92m, 0m, 12345, DateTimeOffset.UtcNow);
        Assert.False(BoardQuoteVerification.HasVerifiedLiveQuote(seed, brokerHasSession: true));

        var live = new BoardQuoteVerification.LiveSymbolFields(
            "ICICIPRULI", 612.5m, 608m, 620m, 605m, 610m, 12345, DateTimeOffset.UtcNow);
        if (NseCashSession.IsLiveIntradayWindowNow())
        {
            Assert.True(BoardQuoteVerification.HasVerifiedLiveQuote(live, brokerHasSession: true));
        }
    }

    [Fact]
    public void HasVerifiedLiveQuote_allows_mock_board_without_broker()
    {
        var seed = new BoardQuoteVerification.LiveSymbolFields(
            "ICICIPRULI", 100m, 100m, 108m, 92m, 0m, 0, null);
        Assert.False(BoardQuoteVerification.HasVerifiedLiveQuote(seed, brokerHasSession: false));
    }

    [Fact]
    public void HasVerifiedLiveQuote_off_session_uses_reference_close_not_placeholder_ltp()
    {
        if (NseCashSession.IsLiveIntradayWindowNow())
        {
            return;
        }

        var row = new BoardQuoteVerification.LiveSymbolFields(
            "MARUTI", 100m, 100m, 12500m, 12380m, 12450m, 12345, DateTimeOffset.UtcNow);
        Assert.True(BoardQuoteVerification.HasVerifiedLiveQuote(row, brokerHasSession: true));
    }
}
