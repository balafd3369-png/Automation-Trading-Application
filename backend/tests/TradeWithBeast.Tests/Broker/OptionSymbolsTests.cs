using TradeWithBeast.Broker;

namespace TradeWithBeast.Tests.Broker;

public sealed class OptionSymbolsTests
{
    [Fact]
    public void QuoteKeys_builds_nfo_aliases_for_nifty_option()
    {
        var expiry = new DateTime(2026, 9, 18);
        var keys = OptionSymbols.QuoteKeys("NIFTY", expiry, 23300m, "CE");
        Assert.Equal(3, keys.Count);
        Assert.All(keys, key => Assert.StartsWith("NFO:", key, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(keys, key => key.Contains("23300CE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QuoteKeys_uses_bfo_for_sensex()
    {
        var expiry = new DateTime(2026, 9, 18);
        var keys = OptionSymbols.QuoteKeys("SENSEX", expiry, 74000m, "PE");
        Assert.All(keys, key => Assert.StartsWith("BFO:", key, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(keys, key => key.Contains("74000PE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QuoteKeys_uses_o_for_october_weekly_code()
    {
        var expiry = new DateTime(2026, 10, 6);
        var keys = OptionSymbols.QuoteKeys("BANKNIFTY", expiry, 51300m, "CE");
        Assert.Contains(keys, key => key.Contains("BANKNIFTY26O0651300CE", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(10, "O")]
    [InlineData(11, "N")]
    [InlineData(12, "D")]
    [InlineData(9, "9")]
    public void MonthCode_matches_mstock_weekly_encoding(int month, string expected)
    {
        Assert.Equal(expected, OptionSymbols.MonthCode(month));
    }

    [Fact]
    public void ExpiryIst_sets_market_close_time()
    {
        var expiry = OptionSymbols.ExpiryIst("2026-09-18");
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var local = TimeZoneInfo.ConvertTime(expiry, ist);
        Assert.Equal(2026, local.Year);
        Assert.Equal(9, local.Month);
        Assert.Equal(18, local.Day);
        Assert.Equal(15, local.Hour);
        Assert.Equal(30, local.Minute);
    }
}
