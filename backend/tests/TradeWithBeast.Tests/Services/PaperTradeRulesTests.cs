using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class PaperTradeRulesTests
{
    [Fact]
    public void DetectZoneCross_requires_price_to_cross_b1_or_s1()
    {
        Assert.Equal(PaperEntrySignal.Long, PaperTradeRules.DetectZoneCross(99m, 101m, 100m, 90m));
        Assert.Equal(PaperEntrySignal.Short, PaperTradeRules.DetectZoneCross(91m, 89m, 100m, 90m));
        Assert.Equal(PaperEntrySignal.None, PaperTradeRules.DetectZoneCross(101m, 102m, 100m, 90m));
    }

    [Fact]
    public void CalculateQuantity_uses_floor_capital_over_entry()
    {
        Assert.Equal(100, PaperTradeRules.CalculateQuantity(100m, 10_000m));
        Assert.Equal(10, PaperTradeRules.CalculateQuantity(1_000m, 10_000m));
        Assert.Equal(0, PaperTradeRules.CalculateQuantity(0m, 10_000m));
        Assert.Equal(0, PaperTradeRules.CalculateQuantity(100m, 0m));
    }

    [Theory]
    [InlineData("LONG", 95, 95, 110, true, false)]
    [InlineData("LONG", 110, 95, 110, false, true)]
    [InlineData("SHORT", 105, 105, 90, true, false)]
    [InlineData("SHORT", 90, 105, 90, false, true)]
    public void Exit_rules_handle_long_and_short_positions(
        string direction,
        decimal current,
        decimal stop,
        decimal target,
        bool expectStop,
        bool expectTarget)
    {
        Assert.Equal(expectStop, PaperTradeRules.HitStop(direction, current, stop));
        Assert.Equal(expectTarget, PaperTradeRules.HitTarget(direction, current, target));
    }

    [Fact]
    public void Approval_rejects_excessive_price_drift_or_exhausted_target()
    {
        Assert.True(PaperTradeRules.ApprovalStillValid("LONG", 100m, 100.40m, 110m));
        Assert.False(PaperTradeRules.ApprovalStillValid("LONG", 100m, 100.60m, 110m));
        Assert.False(PaperTradeRules.ApprovalStillValid("LONG", 100m, 110m, 110m));
        Assert.True(PaperTradeRules.ApprovalStillValid("SHORT", 100m, 99.60m, 90m));
    }
}
