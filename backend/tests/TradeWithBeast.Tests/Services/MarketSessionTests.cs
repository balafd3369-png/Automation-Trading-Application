using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class MarketSessionTests
{
    [Fact]
    public void Weekend_is_reported_as_closed()
    {
        var saturday = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.FromHours(5.5));
        var session = MarketSession.Evaluate(saturday);
        Assert.Equal("WEEKEND", session.Phase);
        Assert.False(session.IsOpen);
    }

    [Fact]
    public void After_close_is_reported_as_closed()
    {
        var evening = new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.FromHours(5.5));
        var session = MarketSession.Evaluate(evening);
        Assert.Equal("CLOSED", session.Phase);
        Assert.False(session.IsOpen);
        Assert.Contains("closed", session.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Midday_is_open_for_entries()
    {
        var midday = new DateTimeOffset(2026, 9, 15, 11, 30, 0, TimeSpan.FromHours(5.5));
        var session = MarketSession.Evaluate(midday);
        Assert.Equal("OPEN", session.Phase);
        Assert.True(session.IsOpen);
    }
}
