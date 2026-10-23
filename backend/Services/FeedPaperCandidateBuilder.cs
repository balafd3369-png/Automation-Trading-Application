using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Builds paper trade candidates from strategy feed cards (same source as the Strategies UI).
/// The decision engine can miss 10 AM setups when live price moves off the signal bar or the session tracker consumes the bar.
/// </summary>
public static class FeedPaperCandidateBuilder
{
    public static StrategyCandidate? FromRecommendation(
        StrategyLiveRecommendation rec,
        IReadOnlyList<MarketContextSnapshot> context)
    {
        var strategyCode = StrategyRecommendationEngine.StrategyCodeFromCatalog(
            StrategyRecommendationEngine.MapStrategyCode(rec.StrategyId));

        return strategyCode switch
        {
            TenAmIntradayPattern.HighFadeStrategy or TenAmIntradayPattern.LowBreakStrategy
                => BuildTenAm(rec, strategyCode, context),
            ZoneRejectionPattern.B1RejectionToS2 => BuildB1(rec, context),
            _ => null,
        };
    }

    private static StrategyCandidate? BuildTenAm(
        StrategyLiveRecommendation rec,
        string strategyCode,
        IReadOnlyList<MarketContextSnapshot> context)
    {
        var isIndex = IndexUniverse.IsIndex(rec.Symbol);
        var side = strategyCode == TenAmIntradayPattern.HighFadeStrategy ? "SELL" : "BUY";
        var direction = rec.Direction;

        var indexSpot = context.FirstOrDefault(x =>
            x.Symbol.Equals(rec.Symbol, StringComparison.OrdinalIgnoreCase)
            && (x.InstrumentType == "INDEX" || x.InstrumentType == "STOCK"));
        if (indexSpot is null || indexSpot.Last <= 0)
        {
            return null;
        }

        var (risk, reward) = Payoff(
            side,
            rec.StopLoss,
            rec.Target,
            indexSpot.Last,
            rec.RiskPoints,
            rec.RewardPoints);
        if (risk <= 0 || reward <= 0)
        {
            return null;
        }

        if (ZoneBoardOptionLegSelector.TryBuildSingleLongOptionLeg(indexSpot, direction, context, out var option)
            && option is not null)
        {
            return new StrategyCandidate(
                strategyCode,
                rec.Symbol,
                direction,
                84m,
                rec.WhyNow,
                [option],
                rec.StopLoss,
                rec.Target,
                risk,
                reward,
                option.Quote?.DataTimestamp ?? indexSpot.Quote.DataTimestamp);
        }

        if (isIndex)
        {
            var future = context.FirstOrDefault(x =>
                x.Symbol.Equals($"{rec.Symbol} FUT", StringComparison.OrdinalIgnoreCase)
                && x.InstrumentType == "FUTURE");
            if (future is null || future.Last <= 0)
            {
                return null;
            }

            return new StrategyCandidate(
                strategyCode,
                rec.Symbol,
                direction,
                84m,
                rec.WhyNow,
                [new(future.Symbol, rec.Symbol, "FUTURE", side, 1, future.Last, Quote: future.Quote)],
                rec.StopLoss,
                rec.Target,
                risk,
                reward,
                future.Quote.DataTimestamp);
        }

        return new StrategyCandidate(
            strategyCode,
            rec.Symbol,
            direction,
            83m,
            rec.WhyNow,
            [new(indexSpot.Symbol, indexSpot.Symbol, "STOCK", side, 1, indexSpot.Last, Quote: indexSpot.Quote)],
            rec.StopLoss,
            rec.Target,
            risk,
            reward,
            indexSpot.Quote.DataTimestamp);
    }

    private static StrategyCandidate? BuildB1(
        StrategyLiveRecommendation rec,
        IReadOnlyList<MarketContextSnapshot> context)
    {
        if (!IndexUniverse.IsIndex(rec.Symbol))
        {
            return null;
        }

        var index = context.FirstOrDefault(x =>
            x.Symbol.Equals(rec.Symbol, StringComparison.OrdinalIgnoreCase)
            && IndexUniverse.IsIndex(x.Symbol));
        if (index is null || index.Last <= 0)
        {
            return null;
        }

        if (!ZoneBoardOptionLegSelector.TryBuildIndexOptionLegs(index, "BEARISH", context, out var legs))
        {
            return null;
        }

        var risk = rec.StopLoss - index.Last;
        var reward = index.Last - rec.Target;
        if (risk <= 0 || reward <= 0)
        {
            risk = rec.RiskPoints;
            reward = rec.RewardPoints;
        }

        if (risk <= 0 || reward <= 0)
        {
            return null;
        }

        var debit = legs.Sum(leg => leg.Price);
        if (debit <= 0)
        {
            return null;
        }

        return new StrategyCandidate(
            ZoneRejectionPattern.B1RejectionToS2,
            rec.Symbol,
            "BEARISH",
            78m,
            rec.WhyNow,
            legs,
            rec.StopLoss,
            rec.Target,
            Math.Max(debit * 0.45m, risk),
            Math.Max(debit * 1.2m, reward),
            legs.Max(l => l.Quote is null || l.Quote.DataTimestamp == default
                ? DateTimeOffset.UtcNow
                : l.Quote.DataTimestamp));
    }

    private static (decimal Risk, decimal Reward) Payoff(
        string side,
        decimal stop,
        decimal target,
        decimal last,
        decimal fallbackRisk,
        decimal fallbackReward)
    {
        decimal risk;
        decimal reward;
        if (side == "SELL")
        {
            risk = stop - last;
            reward = last - target;
        }
        else
        {
            risk = last - stop;
            reward = target - last;
        }

        if (risk <= 0 || reward <= 0)
        {
            risk = fallbackRisk;
            reward = fallbackReward;
        }

        return (risk, reward);
    }
}
