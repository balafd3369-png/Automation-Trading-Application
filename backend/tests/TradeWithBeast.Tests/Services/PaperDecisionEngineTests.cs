using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class PaperDecisionEngineTests
{
    [Fact]
    public void Feature_calculation_exposes_ema_momentum_volatility_zone_and_regime()
    {
        var evidence = MarketContextService.CalculateEvidence(
            [100m, 101m, 102m, 103m, 104m, 105m],
            105m, 100m, 103m, 100m, 97m, 110m, 90m);

        Assert.NotNull(evidence.EmaFast);
        Assert.NotNull(evidence.EmaSlow);
        Assert.True(evidence.EmaFast > evidence.EmaSlow);
        Assert.True(evidence.MomentumPercent > 0);
        Assert.NotNull(evidence.RealizedVolatilityPercent);
        Assert.Equal("UP", evidence.Trend);
        Assert.Equal("ABOVE_B1", evidence.Zone);
        Assert.Contains(evidence.Facts, x => x.StartsWith("B1="));
    }

    [Fact]
    public void Missing_history_is_reported_as_unknown_not_invented()
    {
        var evidence = MarketContextService.CalculateEvidence([100m], 100m, 100m, 105m, 100m, 95m, 110m, 90m);

        Assert.Null(evidence.EmaFast);
        Assert.Null(evidence.MomentumPercent);
        Assert.Null(evidence.RealizedVolatilityPercent);
        Assert.Equal("UNKNOWN", evidence.Trend);
        Assert.Equal("UNKNOWN", evidence.Regime);
    }

    [Fact]
    public void Stock_strategy_requires_an_actual_long_zone_cross()
    {
        var context = Context(
            "INFY", "STOCK", 106m, "UP", "TRENDING",
            ["todayOpen=100", "B1=105", "S=100", "S1=95", "B2=115", "S2=90"]);
        var sessions = new ZoneBoardSessionTracker();
        sessions.Update("INFY", 104m, 105m, 95m, 90m, new DateOnly(2026, 9, 15));
        sessions.Update("INFY", 106m, 105m, 95m, 90m, new DateOnly(2026, 9, 15));

        var candidates = new ZoneBoardIntradayStrategy(sessions).Generate(
            [context], new Dictionary<string, decimal> { ["INFY"] = 104m }).ToList();

        var candidate = Assert.Single(candidates);
        Assert.Equal(ZoneBoardPattern.B1BreakLong, candidate.Strategy);
        Assert.Equal("BUY", Assert.Single(candidate.Legs).Side);
        Assert.True(candidate.MaxRiskPerUnit > 0);
    }

    [Fact]
    public void Recorded_context_replay_produces_the_same_ranked_decision()
    {
        var recordedTick = Context(
            "INFY", "STOCK", 106m, "UP", "TRENDING",
            ["todayOpen=100", "B1=105", "S=100", "S1=95", "B2=115", "S2=90"]);
        var previous = new Dictionary<string, decimal> { ["INFY"] = 104m };
        var sessions = new ZoneBoardSessionTracker();
        sessions.Update("INFY", 104m, 105m, 95m, 90m, new DateOnly(2026, 9, 15));
        sessions.Update("INFY", 106m, 105m, 95m, 90m, new DateOnly(2026, 9, 15));
        var engine = new PaperDecisionEngine([new ZoneBoardIntradayStrategy(sessions)]);

        var first = Assert.Single(engine.Rank([recordedTick], previous));
        var replay = Assert.Single(engine.Rank([recordedTick], previous));

        Assert.Equal(first.Strategy, replay.Strategy);
        Assert.Equal(first.Underlying, replay.Underlying);
        Assert.Equal(first.EvidenceScore, replay.EvidenceScore);
        Assert.Equal(first.Evidence, replay.Evidence);
        Assert.Equal(first.Legs.Select(leg => leg.Symbol), replay.Legs.Select(leg => leg.Symbol));
        Assert.Equal(ZoneBoardPattern.B1BreakLong, replay.Strategy);
    }

    [Fact]
    public void Option_strategy_generates_long_and_defined_risk_verticals()
    {
        var index = Context("NIFTY", "INDEX", 25000m, "UP", "TRENDING", []);
        var expiry = new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.FromHours(5.5));
        var options = new[]
        {
            Option("NIFTY 20260917 25000 CE", 200m, 25000m, expiry),
            Option("NIFTY 20260917 25050 CE", 170m, 25050m, expiry),
            Option("NIFTY 20260917 25000 PE", 180m, 25000m, expiry),
            Option("NIFTY 20260917 24950 PE", 150m, 24950m, expiry),
        };

        var candidates = new IndexOptionStrategy().Generate([index, .. options], new Dictionary<string, decimal>()).ToList();

        Assert.Contains(candidates, x => x.Strategy == "INDEX_OPTION_LONG");
        var debit = Assert.Single(candidates, x => x.Strategy == "VERTICAL_DEBIT_SPREAD");
        Assert.Equal(2, debit.Legs.Count);
        Assert.Contains(debit.Legs, x => x.Side == "BUY");
        Assert.Contains(debit.Legs, x => x.Side == "SELL");
        Assert.DoesNotContain(candidates, x => x.Strategy == "HEDGED_VERTICAL_CREDIT_SPREAD");
    }

    [Fact]
    public void Volatile_regime_prefers_a_hedged_credit_candidate()
    {
        var index = Context("NIFTY", "INDEX", 25000m, "UP", "VOLATILE", []);
        var expiry = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.FromHours(5.5));
        var options = new[]
        {
            Option("NIFTY 20260922 25000 CE", 200m, 25000m, expiry),
            Option("NIFTY 20260922 25050 CE", 170m, 25050m, expiry),
            Option("NIFTY 20260922 25000 PE", 180m, 25000m, expiry),
            Option("NIFTY 20260922 24950 PE", 150m, 24950m, expiry),
        };

        var candidates = new IndexOptionStrategy()
            .Generate([index, .. options], new Dictionary<string, decimal>())
            .ToList();

        var credit = Assert.Single(candidates, x => x.Strategy == "HEDGED_VERTICAL_CREDIT_SPREAD");
        Assert.Contains(credit.Legs, x => x.Side == "SELL");
        Assert.Contains(credit.Legs, x => x.Side == "BUY");
    }

    [Fact]
    public void Range_regime_can_generate_a_risk_sized_naked_index_short()
    {
        var index = Context("NIFTY", "INDEX", 25000m, "DOWN", "RANGE", []);
        var expiry = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.FromHours(5.5));
        var options = new[]
        {
            Option("NIFTY 20260922 25050 CE", 100m, 25050m, expiry),
            Option("NIFTY 20260922 25100 CE", 80m, 25100m, expiry),
            Option("NIFTY 20260922 24950 PE", 90m, 24950m, expiry),
        };

        var candidates = new IndexOptionStrategy()
            .Generate([index, .. options], new Dictionary<string, decimal>())
            .ToList();
        var naked = Assert.Single(
            candidates,
            candidate => candidate.Strategy == "NAKED_INDEX_OPTION_SHORT");

        Assert.Equal("SELL", Assert.Single(naked.Legs).Side);
        Assert.Equal("NIFTY", naked.Underlying);
    }

    [Fact]
    public void Stock_range_regime_can_generate_naked_stock_option_short()
    {
        var stock = Context("RELIANCE", "STOCK", 2800m, "UP", "RANGE", ["B1=2790", "S=2770", "S1=2760", "B2=2850", "S2=2740"]);
        var expiry = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.FromHours(5.5));
        var options = new[]
        {
            Option("RELIANCE 20260922 2750 PE", 40m, 2750m, expiry),
            Option("RELIANCE 20260922 2700 PE", 30m, 2700m, expiry),
        };

        var candidates = new StockNakedOptionStrategy()
            .Generate([stock, .. options], new Dictionary<string, decimal>())
            .ToList();

        var naked = Assert.Single(candidates, x => x.Strategy == "NAKED_STOCK_OPTION_SHORT");
        Assert.Equal("RELIANCE", naked.Underlying);
    }

    [Fact]
    public void Trending_regime_can_generate_naked_index_future()
    {
        var index = Context("BANKNIFTY", "INDEX", 52000m, "UP", "TRENDING", ["B1=51900", "S=51700", "S1=51600", "B2=52500", "S2=51400"]);
        var future = Context("BANKNIFTY FUT", "FUTURE", 52010m, "UP", "TRENDING", index.Evidence.Facts);

        var candidates = new NakedFutureStrategy()
            .Generate([index, future], new Dictionary<string, decimal>())
            .ToList();

        var naked = Assert.Single(candidates, x => x.Strategy == "NAKED_INDEX_FUTURE");
        Assert.Equal("BUY", Assert.Single(naked.Legs).Side);
    }

    [Theory]
    [InlineData("NIFTY", 65)]
    [InlineData("BANKNIFTY", 30)]
    [InlineData("FINNIFTY", 60)]
    [InlineData("SENSEX", 20)]
    public void Uses_current_2026_option_lots(string index, int lot) =>
        Assert.Equal(lot, PortfolioRiskManager.LotSize(index));

    [Theory]
    [InlineData("NIFTY", 22)]
    [InlineData("FINNIFTY", 22)]
    [InlineData("BANKNIFTY", 16)]
    [InlineData("SENSEX", 18)]
    public void Resolves_exchange_specific_weekly_expiry(string underlying, int expectedDay)
    {
        var now = IstTime(2026, 9, 15, 17, 0);

        Assert.Equal(
            expectedDay,
            MarketContextService.NextWeeklyExpiry(underlying, now).Day);
    }

    [Fact]
    public void Vertical_payoff_math_is_bounded()
    {
        Assert.Equal((30m, 70m), PortfolioRiskManager.VerticalPayoff(100m, 30m, true));
        Assert.Equal((70m, 30m), PortfolioRiskManager.VerticalPayoff(100m, 30m, false));
        Assert.Equal((0m, 0m), PortfolioRiskManager.VerticalPayoff(100m, 100m, true));
    }

    [Fact]
    public void Naked_index_option_passes_risk_when_liquid()
    {
        var quote = new QuoteMetadata("test", "NFO", null, DateTimeOffset.UtcNow, 1, true, 99m, 100m, 1000, 5000, []);
        var candidate = new StrategyCandidate(
            "NAKED_INDEX_OPTION_SHORT", "NIFTY", "BEARISH", 72, ["gap risk"],
            [new("NIFTY CE", "NIFTY", "OPTION", "SELL", 1, 100m, Quote: quote)],
            150m, 50m, 50m, 50m, DateTimeOffset.UtcNow);

        var result = new PortfolioRiskManager().Assess(
            candidate, new(100_000m, 2_000m, 10_000m, 3), 0, 0, 0, [],
            IstTime(2026, 9, 16, 10, 0));

        Assert.True(result.Accepted);
    }

    [Fact]
    public void Risk_rejects_stale_missing_liquidity_and_naked_credit()
    {
        var quote = new QuoteMetadata("test", "NFO", null, DateTimeOffset.UtcNow, 99, false, null, null, null, null,
            ["bid", "ask", "volume", "openInterest"]);
        var candidate = new StrategyCandidate(
            "HEDGED_VERTICAL_CREDIT_SPREAD", "NIFTY", "BULLISH", 50, [],
            [new("NIFTY CE", "NIFTY", "OPTION", "SELL", 1, 100m, Quote: quote)],
            150m, 50m, 50m, 50m, DateTimeOffset.UtcNow);

        var result = new PortfolioRiskManager().Assess(
            candidate, new(100_000m, 2_000m, 1_000m, 3), 0, 0, 0, [],
            IstTime(2026, 9, 16, 10, 0));

        Assert.False(result.Accepted);
        Assert.Contains(result.Rejections, x => x.Contains("stale"));
        Assert.Contains(result.Rejections, x => x.Contains("bid/ask"));
        Assert.Contains(result.Rejections, x => x.Contains("naked"));
    }

    [Fact]
    public void Risk_sizes_liquid_option_in_whole_lots()
    {
        var quote = new QuoteMetadata("test", "NFO", null, DateTimeOffset.UtcNow, 1, true, 99m, 100m, 1000, 5000, []);
        var candidate = new StrategyCandidate(
            "VERTICAL_DEBIT_SPREAD", "NIFTY", "BULLISH", 75, ["defined risk"],
            [
                new("NIFTY 25000 CE", "NIFTY", "OPTION", "BUY", 1, 100m, Quote: quote),
                new("NIFTY 25100 CE", "NIFTY", "OPTION", "SELL", 1, 60m, Quote: quote),
            ],
            20m, 80m, 40m, 60m, DateTimeOffset.UtcNow);

        var result = new PortfolioRiskManager().Assess(
            candidate, new(100_000m, 2_000m, 10_000m, 3), 0, 0, 0, [],
            IstTime(2026, 9, 16, 10, 0));

        Assert.True(result.Accepted);
        Assert.True(result.QuantityMultiplier >= 65);
        Assert.Equal(0, result.QuantityMultiplier % 65);
    }

    [Fact]
    public void Hedged_spread_requires_one_lakh_and_allows_only_one_open_spread()
    {
        var quote = new QuoteMetadata("test", "NFO", null, DateTimeOffset.UtcNow, 1, true, 99m, 100m, 1000, 5000, []);
        var candidate = new StrategyCandidate(
            "VERTICAL_DEBIT_SPREAD", "NIFTY", "BULLISH", 80, ["defined risk"],
            [
                new("NIFTY 25000 CE", "NIFTY", "OPTION", "BUY", 1, 100m, Quote: quote),
                new("NIFTY 25050 CE", "NIFTY", "OPTION", "SELL", 1, 70m, Quote: quote),
            ],
            15m, 40m, 30m, 20m, DateTimeOffset.UtcNow);
        var risk = new PortfolioRiskManager();
        var now = IstTime(2026, 9, 16, 10, 0);

        var underfunded = risk.Assess(
            candidate, new(99_999m, 2_000m, 10_000m, 3), 0, 0, 0, [], now);
        var secondHedge = risk.Assess(
            candidate, new(100_000m, 2_000m, 10_000m, 3), 0, 2_000m, 1,
            [new("BANKNIFTY", "BEARISH", 2_000m, "HEDGED_VERTICAL_CREDIT_SPREAD")], now);

        Assert.Contains(underfunded.Rejections, rejection => rejection.Contains("₹1,00,000"));
        Assert.Contains(secondHedge.Rejections, rejection => rejection.Contains("only one"));
    }

    private static MarketContextSnapshot Context(
        string symbol,
        string type,
        decimal last,
        string trend,
        string regime,
        IReadOnlyList<string> facts) =>
        new(symbol, type, last, Quote(), new(null, null, 1m, 10m, trend, "ABOVE_B1", regime, facts));

    private static MarketContextSnapshot Option(string symbol, decimal last, decimal strike, DateTimeOffset expiry) =>
        new(symbol, "OPTION", last, Quote(last), new(null, null, 1m, null, "UP", "OPTION_PREMIUM", "DERIVATIVE",
            [$"strike={strike}", "right=CE", $"expiry={expiry:O}"]));

    private static QuoteMetadata Quote(decimal last = 100m) =>
        new("test", "NFO", null, DateTimeOffset.UtcNow, 0, true, last - 1m, last + 1m, 1000, 5000, []);

    private static DateTimeOffset IstTime(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(5.5));
}
