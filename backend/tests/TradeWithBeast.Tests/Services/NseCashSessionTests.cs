using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class NseCashSessionTests
{
    [Fact]
    public void Saturday_during_session_hours_is_not_live_intraday()
    {
        var ist = new DateTimeOffset(2026, 10, 24, 10, 30, 0, TimeSpan.FromHours(5.5));
        Assert.False(NseCashSession.IsLiveIntradayWindow(ist));
        Assert.False(NseCashSession.IsTradingDay(DateOnly.FromDateTime(ist.Date)));
    }

    [Fact]
    public void Friday_during_session_hours_is_live_intraday()
    {
        var ist = new DateTimeOffset(2026, 10, 23, 10, 30, 0, TimeSpan.FromHours(5.5));
        Assert.True(NseCashSession.IsTradingDay(DateOnly.FromDateTime(ist.Date)));
        Assert.True(NseCashSession.IsLiveIntradayWindow(ist));
    }
}
