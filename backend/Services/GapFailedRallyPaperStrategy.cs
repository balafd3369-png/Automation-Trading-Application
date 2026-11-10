using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class GapFailedRallyPaperStrategy(
    GapSessionTracker sessions,
    GapAnalyzerBenchmarkCache benchmarks) : IPaperStrategy
{
    public IEnumerable<StrategyCandidate> Generate(
        IReadOnlyList<MarketContextSnapshot> context,
        IReadOnlyDictionary<string, decimal> previous)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var istDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist).DateTime);
        var featured = benchmarks.Snapshot.FeaturedClue;
        var universeWin = featured?.TradeWinRatePercent ?? 0m;

        foreach (var stock in context.Where(x => x.InstrumentType == "STOCK" && !IndexUniverse.IsIndex(x.Symbol)))
        {
            if (!sessions.TryPeekPending(stock.Symbol, istDay, out var signal))
            {
                continue;
            }

            benchmarks.Snapshot.FailedRallyBySymbol.TryGetValue(stock.Symbol, out var hist);
            var histTrades = (hist?.TradeWins ?? 0) + (hist?.TradeLosses ?? 0);
            var histWin = histTrades >= 3 ? hist!.TradeWinRatePercent : universeWin;
            if (histTrades >= 3 && histWin < ZoneBoardPattern.MinWinRateToRecommend - 10m)
            {
                continue;
            }

            var entry = stock.Last > 0 ? stock.Last : signal.Entry;
            var score = 70m + Math.Min(25m, histWin * 0.3m);
            var evidence = signal.Evidence
                .Concat([
                    $"paper strategy {StrategyCatalog.GapFailedRallyGapDownId}",
                    histTrades >= 3
                        ? $"symbol backtest {histWin:0.#}% ({hist!.TradeWins}W/{hist.TradeLosses}L)"
                        : $"universe backtest ~{universeWin:0.#}%",
                    $"triggered {TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(signal.SignalBarUnix), ist):HH:mm} IST",
                ])
                .ToList();

            var bullish = signal.Strategy == GapDayStudy.ClueStrongCloseGapUp;
            var side = bullish ? "BUY" : "SELL";
            var catalogId = signal.Strategy switch
            {
                GapDayStudy.ClueDistributionGapDown => StrategyCatalog.GapDistributionGapDownId,
                GapDayStudy.ClueWeakCloseGapDown => StrategyCatalog.GapWeakCloseGapDownId,
                GapDayStudy.ClueStrongCloseGapUp => StrategyCatalog.GapStrongCloseGapUpId,
                _ => StrategyCatalog.GapFailedRallyGapDownId,
            };

            yield return new StrategyCandidate(
                signal.Strategy,
                stock.Symbol,
                bullish ? "BULLISH" : "BEARISH",
                score,
                evidence
                    .Concat([$"catalog {catalogId}"])
                    .ToList(),
                [new(stock.Symbol, stock.Symbol, "STOCK", side, 1, entry, Quote: stock.Quote)],
                signal.StopLoss,
                signal.Target,
                signal.RiskPoints,
                signal.RewardPoints,
                stock.Quote.DataTimestamp == default ? DateTimeOffset.UtcNow : stock.Quote.DataTimestamp);
            sessions.MarkConsumed(stock.Symbol, istDay);
        }
    }
}
