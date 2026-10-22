using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

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

        foreach (var stock in context.Where(x => x.InstrumentType == "STOCK"))
        {
            if (!ZoneRejectionPattern.TryReadZoneFact(stock.Evidence.Facts, "B1=", out var b1)
                || !ZoneRejectionPattern.TryReadZoneFact(stock.Evidence.Facts, "S2=", out var s2)
                || !ZoneRejectionPattern.TryReadZoneFact(stock.Evidence.Facts, "S=", out var stopLine))
            {
                continue;
            }

            if (!sessions.TryTakePendingRejection(stock.Symbol, istDay))
            {
                continue;
            }

            var backtest = LoadBacktest(stock.Symbol, istDay, b1, s2);
            if (!ZoneRejectionPattern.QualifiesForUserRecommendation(backtest))
            {
                continue;
            }

            var entry = stock.Last;
            var stop = Math.Max(b1, entry) * 1.0015m;
            var risk = stop - entry;
            var reward = entry - s2;
            if (risk <= 0 || reward <= 0)
            {
                continue;
            }

            yield return new StrategyCandidate(
                ZoneRejectionPattern.B1RejectionToS2,
                stock.Symbol,
                "BEARISH",
                88m + Math.Min(10m, (backtest.WinRatePercent - 90m) / 2m),
                [
                    $"B1 rejection after touch ({b1:0.##})",
                    $"backtest win rate {backtest.WinRatePercent:0.##}% over {backtest.Samples} samples",
                    $"target S2 {s2:0.##}",
                    $"reference pivot S {stopLine:0.##}",
                ],
                [new(stock.Symbol, stock.Symbol, "STOCK", "SELL", 1, entry, Quote: stock.Quote)],
                stop,
                s2,
                risk,
                reward,
                stock.Quote.DataTimestamp);
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
