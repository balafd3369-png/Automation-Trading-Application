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

        if (isIndex)
        {
            var future = context.FirstOrDefault(x =>
                x.Symbol.Equals($"{rec.Symbol} FUT", StringComparison.OrdinalIgnoreCase)
                && x.InstrumentType == "FUTURE");
            if (future is null || future.Last <= 0)
            {
                return null;
            }

            var (risk, reward) = Payoff(side, rec.StopLoss, rec.Target, future.Last, rec.RiskPoints, rec.RewardPoints);
            if (risk <= 0 || reward <= 0)
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

        var spot = context.FirstOrDefault(x =>
            x.Symbol.Equals(rec.Symbol, StringComparison.OrdinalIgnoreCase)
            && x.InstrumentType == "STOCK");
        if (spot is null || spot.Last <= 0)
        {
            return null;
        }

        var (stockRisk, stockReward) = Payoff(side, rec.StopLoss, rec.Target, spot.Last, rec.RiskPoints, rec.RewardPoints);
        if (stockRisk <= 0 || stockReward <= 0)
        {
            return null;
        }

        return new StrategyCandidate(
            strategyCode,
            rec.Symbol,
            direction,
            83m,
            rec.WhyNow,
            [new(spot.Symbol, spot.Symbol, "STOCK", side, 1, spot.Last, Quote: spot.Quote)],
            rec.StopLoss,
            rec.Target,
            stockRisk,
            stockReward,
            spot.Quote.DataTimestamp);
    }

    private static StrategyCandidate? BuildB1(
        StrategyLiveRecommendation rec,
        IReadOnlyList<MarketContextSnapshot> context)
    {
        var spot = context.FirstOrDefault(x =>
            x.Symbol.Equals(rec.Symbol, StringComparison.OrdinalIgnoreCase)
            && x.InstrumentType == "STOCK");
        if (spot is null || spot.Last <= 0)
        {
            return null;
        }

        var risk = rec.StopLoss - spot.Last;
        var reward = spot.Last - rec.Target;
        if (risk <= 0 || reward <= 0)
        {
            risk = rec.RiskPoints;
            reward = rec.RewardPoints;
        }

        if (risk <= 0 || reward <= 0)
        {
            return null;
        }

        return new StrategyCandidate(
            ZoneRejectionPattern.B1RejectionToS2,
            rec.Symbol,
            "BEARISH",
            78m,
            rec.WhyNow,
            [new(spot.Symbol, spot.Symbol, "STOCK", "SELL", 1, spot.Last, Quote: spot.Quote)],
            rec.StopLoss,
            rec.Target,
            risk,
            reward,
            spot.Quote.DataTimestamp);
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
