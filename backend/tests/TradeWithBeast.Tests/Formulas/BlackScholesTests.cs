using TradeWithBeast.Formulas;

namespace TradeWithBeast.Tests.Formulas;

public sealed class BlackScholesTests
{
    [Theory]
    [InlineData("NIFTY", 0.13)]
    [InlineData("BANKNIFTY", 0.18)]
    [InlineData("FINNIFTY", 0.16)]
    public void ImpliedVol_matches_underlying_defaults(string underlying, double expected)
    {
        Assert.Equal(expected, BlackScholes.ImpliedVol(underlying));
    }

    [Fact]
    public void Premium_for_atm_call_is_positive_and_tick_rounded()
    {
        var expiry = DateTimeOffset.UtcNow.AddDays(7);
        var premium = BlackScholes.Premium(23300m, 23300m, isCall: true, expiry, "NIFTY");
        Assert.True(premium >= 0.05m);
        Assert.Equal(0, premium % 0.05m);
    }

    [Fact]
    public void Premium_for_otm_put_is_positive()
    {
        var expiry = DateTimeOffset.UtcNow.AddDays(7);
        var premium = BlackScholes.Premium(23300m, 22750m, isCall: false, expiry, "NIFTY");
        Assert.True(premium >= 0.05m);
    }

    [Fact]
    public void Premium_returns_zero_for_invalid_inputs()
    {
        var expiry = DateTimeOffset.UtcNow.AddDays(7);
        Assert.Equal(0.05m, BlackScholes.Premium(0m, 23000m, true, expiry, "NIFTY"));
    }
}
