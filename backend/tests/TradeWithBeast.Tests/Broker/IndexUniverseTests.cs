using TradeWithBeast.Broker;

namespace TradeWithBeast.Tests.Broker;

public sealed class IndexUniverseTests
{
    [Theory]
    [InlineData(26000, "NSE", "NIFTY")]
    [InlineData(26009, "NSE", "BANKNIFTY")]
    [InlineData(26037, "NSE", "FINNIFTY")]
    public void TryResolveBoardByInstrumentToken_maps_nse_indices(int token, string exchange, string expected)
    {
        Assert.True(IndexUniverse.TryResolveBoardByInstrumentToken(token, exchange, out var name));
        Assert.Equal(expected, name, ignoreCase: true);
    }

    [Fact]
    public void AllNames_includes_indices_and_equities()
    {
        Assert.Contains("NIFTY", IndexUniverse.AllNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("RELIANCE", IndexUniverse.AllNames, StringComparer.OrdinalIgnoreCase);
        Assert.True(IndexUniverse.AllNames.Count > 60);
    }

    [Theory]
    [InlineData("NIFTY", true)]
    [InlineData("RELIANCE", false)]
    [InlineData("SENSEX", true)]
    public void IsIndex_identifies_index_names(string name, bool expected)
    {
        Assert.Equal(expected, IndexUniverse.IsIndex(name));
    }

    [Fact]
    public void QuoteKeys_maps_nifty_to_nse_index_symbol()
    {
        Assert.Contains("NSE:NIFTY 50", IndexUniverse.QuoteKeys["NIFTY"]);
    }

    [Fact]
    public void QuoteKeys_maps_sensex_and_finnifty_with_fallback_aliases()
    {
        Assert.Contains("BSE:SENSEX", IndexUniverse.QuoteKeys["SENSEX"]);
        Assert.Contains("BSE:SENSEX 50", IndexUniverse.QuoteKeys["SENSEX"]);
        Assert.Contains("NSE:FINNIFTY", IndexUniverse.QuoteKeys["FINNIFTY"]);
    }

    [Fact]
    public void QuoteKeys_maps_equity_to_nse_eq_symbol()
    {
        Assert.Contains("NSE:RELIANCE-EQ", IndexUniverse.QuoteKeys["RELIANCE"]);
    }
}
