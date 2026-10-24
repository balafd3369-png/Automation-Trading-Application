using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class ChartBarSanitizerTests
{
    [Fact]
    public void Does_not_patch_last_bar_with_live_ltp_when_market_closed()
    {
        var friday = new DateTimeOffset(2026, 10, 23, 15, 10, 0, TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();
        var bars = new List<ChartBar>
        {
            new(friday, 22480m, 22524m, 22476m, 22517m),
        };
        var sanitized = ChartBarSanitizer.SanitizeIntraday(bars, "5minute", 22786m);
        Assert.Equal(22517m, sanitized[^1].Close);
        Assert.True(sanitized[^1].High < 22600m);
    }

    [Fact]
    public void Clamps_absurd_wick_on_historical_bar()
    {
        var t = new DateTimeOffset(2026, 10, 23, 15, 10, 0, TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();
        var bar = new ChartBar(t, 22480m, 22904m, 22476m, 22517m);
        var clamped = ChartBarSanitizer.ClampWicks(bar, "5minute");
        Assert.True(clamped.High < 22650m);
        Assert.Equal(22517m, clamped.Close);
    }
}
