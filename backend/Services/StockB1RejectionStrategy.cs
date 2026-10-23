using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>B1 touch-and-reject toward S2 — indices only (PE options), same universe as zone board.</summary>
public sealed class StockB1RejectionStrategy(
    ZoneSessionTracker sessions,
    MockMarketService market) : IPaperStrategy
{
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, (DateOnly Day, ZoneRejectionBacktestResult Result)> _backtests = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<StrategyCandidate> Generate(
        IReadOnlyList<MarketContextSnapshot> context,
        IReadOnlyDictionary<string, decimal> previous)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var istDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist).DateTime);

        foreach (var index in context.Where(x => IndexUniverse.IsIndex(x.Symbol)))
        {
            if (!ZoneRejectionPattern.TryReadZoneFact(index.Evidence.Facts, "B1=", out var b1)
                || !ZoneRejectionPattern.TryReadZoneFact(index.Evidence.Facts, "S2=", out var s2)
                || !ZoneRejectionPattern.TryReadZoneFact(index.Evidence.Facts, "S=", out var stopLine))
            {
                continue;
            }

            if (!sessions.TryTakePendingRejection(index.Symbol, istDay))
            {
                continue;
            }

            var backtest = LoadBacktest(index.Symbol, istDay, b1, s2);
            if (!ZoneRejectionPattern.QualifiesForUserRecommendation(backtest))
            {
                continue;
            }

            if (!ZoneBoardOptionLegSelector.TryBuildIndexOptionLegs(index, "BEARISH", context, out var legs))
            {
                continue;
            }

            var entry = index.Last;
            var stop = Math.Max(b1, entry) * 1.0015m;
            var risk = stop - entry;
            var reward = entry - s2;
            if (risk <= 0 || reward <= 0)
            {
                continue;
            }

            var debit = legs.Sum(leg => leg.Price);
            if (debit <= 0)
            {
                continue;
            }

            var at = index.Quote.DataTimestamp == default ? DateTimeOffset.UtcNow : index.Quote.DataTimestamp;
            var plan = ZoneBoardOptionEstimator.PlanStrikes(index.Symbol, "BEARISH", index.Last, at);
            var score = 88m + Math.Min(10m, (backtest.WinRatePercent - 90m) / 2m);

            yield return new StrategyCandidate(
                ZoneRejectionPattern.B1RejectionToS2,
                index.Symbol,
                "BEARISH",
                score,
                [
                    $"B1 rejection after touch ({b1:0.##})",
                    $"backtest win rate {backtest.WinRatePercent:0.##}% over {backtest.Samples} samples",
                    $"target S2 {s2:0.##}",
                    $"reference pivot S {stopLine:0.##}",
                    $"options ITM {plan.ItmStrike} + OTM {plan.OtmStrike} PE",
                ],
                legs,
                stop,
                s2,
                Math.Max(debit * 0.45m, risk),
                Math.Max(debit * 1.2m, reward),
                legs.Max(l => l.Quote is null || l.Quote.DataTimestamp == default
                    ? DateTimeOffset.UtcNow
                    : l.Quote.DataTimestamp));
        }
    }

    private ZoneRejectionBacktestResult LoadBacktest(string symbol, DateOnly istDay, decimal b1, decimal s2)
    {
        lock (_cacheGate)
        {
            if (_backtests.TryGetValue(symbol, out var cached) && cached.Day == istDay)
            {
                return cached.Result;
            }
        }

        ZoneRejectionBacktestResult result;
        try
        {
            var chart = market.ChartAsync(symbol, "15minute", CancellationToken.None).GetAwaiter().GetResult();
            if (chart is null)
            {
                result = new(symbol, ZoneRejectionPattern.B1RejectionToS2, 0, 0, 0, 0, 0, 0);
            }
            else
            {
                var board = ZoneBoardPattern.BacktestB1RejectionShort(symbol, chart.Candles);
                result = new(
                    board.Symbol,
                    board.Pattern,
                    board.Samples,
                    board.Wins,
                    board.Losses,
                    board.WinRatePercent,
                    board.AvgRewardPoints,
                    board.AvgRiskPoints);
            }
        }
        catch
        {
            result = new(symbol, ZoneRejectionPattern.B1RejectionToS2, 0, 0, 0, 0, 0, 0);
        }

        lock (_cacheGate)
        {
            _backtests[symbol] = (istDay, result);
        }

        return result;
    }
}
