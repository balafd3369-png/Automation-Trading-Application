using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class TenAmIntradayStrategy(
    MockMarketService market,
    TenAmSessionTracker sessions) : IPaperStrategy
{
    private static readonly string[] IndexSymbols = ["NIFTY", "BANKNIFTY", "FINNIFTY", "SENSEX"];

    public IEnumerable<StrategyCandidate> Generate(
        IReadOnlyList<MarketContextSnapshot> context,
        IReadOnlyDictionary<string, decimal> previous)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var istDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist).DateTime);
        var now = DateTimeOffset.UtcNow;
        var board = market.Snapshot();

        foreach (var indexName in IndexSymbols)
        {
            var spot = context.FirstOrDefault(x =>
                x.Symbol.Equals(indexName, StringComparison.OrdinalIgnoreCase)
                && x.InstrumentType == "INDEX");
            if (spot is null)
            {
                continue;
            }

            var future = context.FirstOrDefault(x =>
                x.Symbol.Equals($"{indexName} FUT", StringComparison.OrdinalIgnoreCase)
                && x.InstrumentType == "FUTURE");
            if (future is null || future.Last <= 0)
            {
                continue;
            }

            ChartResponse? chart;
            try
            {
                chart = market
                    .ChartAsync(indexName, TenAmIntradayPattern.ChartInterval, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                continue;
            }

            if (chart is null || chart.Candles.Count == 0)
            {
                continue;
            }

            var dayOpen = board.Symbols.FirstOrDefault(x =>
                x.Name.Equals(indexName, StringComparison.OrdinalIgnoreCase))?.TodayOpen ?? 0;
            if (dayOpen <= 0)
            {
                dayOpen = ResolveDayOpenFromChart(chart);
            }
            if (dayOpen <= 0)
            {
                continue;
            }

            if (TenAmIntradayPattern.TryBuildHighFade(indexName, chart.Candles, dayOpen, now, out var fade)
                && fade is not null)
            {
                var risk = fade.StopLoss - spot.Last;
                var reward = spot.Last - fade.Target;
                if (risk > 0 && reward > 0
                    && sessions.TryTakeSignal(indexName, istDay, fade.Strategy, fade.SignalBarTime)
                    && TryBuildTenAmLegs(spot, context, "BEARISH", future, out var fadeLegs, out var fadeAt))
                {
                    yield return new StrategyCandidate(
                        fade.Strategy,
                        indexName,
                        "BEARISH",
                        82m,
                        fade.Evidence,
                        fadeLegs,
                        fade.StopLoss,
                        fade.Target,
                        risk,
                        reward,
                        fadeAt);
                }
            }

            if (TenAmIntradayPattern.TryBuildLowBreak(indexName, chart.Candles, dayOpen, now, out var lift)
                && lift is not null)
            {
                var risk = spot.Last - lift.StopLoss;
                var reward = lift.Target - spot.Last;
                if (risk > 0 && reward > 0
                    && sessions.TryTakeSignal(indexName, istDay, lift.Strategy, lift.SignalBarTime)
                    && TryBuildTenAmLegs(spot, context, "BULLISH", future, out var liftLegs, out var liftAt))
                {
                    yield return new StrategyCandidate(
                        lift.Strategy,
                        indexName,
                        "BULLISH",
                        81m,
                        lift.Evidence,
                        liftLegs,
                        lift.StopLoss,
                        lift.Target,
                        risk,
                        reward,
                        liftAt);
                }
            }
        }

        foreach (var stock in board.Symbols.Where(s =>
                     IndexUniverse.Nifty50.Contains(s.Name, StringComparer.OrdinalIgnoreCase)
                     && !IndexUniverse.IsIndex(s.Name)))
        {
            var spot = context.FirstOrDefault(x =>
                x.Symbol.Equals(stock.Name, StringComparison.OrdinalIgnoreCase) && x.InstrumentType == "STOCK");
            if (spot is null || spot.Last <= 0)
            {
                continue;
            }

            ChartResponse? chart;
            try
            {
                chart = market.ChartAsync(stock.Name, TenAmIntradayPattern.ChartInterval, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                continue;
            }

            if (chart is null || chart.Candles.Count == 0)
            {
                continue;
            }

            var dayOpen = stock.TodayOpen > 0 ? stock.TodayOpen : ResolveDayOpenFromChart(chart);
            if (dayOpen <= 0)
            {
                continue;
            }

            if (TenAmIntradayPattern.TryBuildHighFade(stock.Name, chart.Candles, dayOpen, now, out var fade)
                && fade is not null)
            {
                var risk = fade.StopLoss - spot.Last;
                var reward = spot.Last - fade.Target;
                if (risk > 0 && reward > 0
                    && sessions.TryTakeSignal(stock.Name, istDay, fade.Strategy, fade.SignalBarTime)
                    && TryBuildTenAmLegs(spot, context, "BEARISH", null, out var fadeLegs, out var fadeAt))
                {
                    yield return new StrategyCandidate(
                        fade.Strategy,
                        stock.Name,
                        "BEARISH",
                        80m,
                        fade.Evidence,
                        fadeLegs,
                        fade.StopLoss,
                        fade.Target,
                        risk,
                        reward,
                        fadeAt);
                }
            }

            if (TenAmIntradayPattern.TryBuildLowBreak(stock.Name, chart.Candles, dayOpen, now, out var lift)
                && lift is not null)
            {
                var risk = spot.Last - lift.StopLoss;
                var reward = lift.Target - spot.Last;
                if (risk > 0 && reward > 0
                    && sessions.TryTakeSignal(stock.Name, istDay, lift.Strategy, lift.SignalBarTime)
                    && TryBuildTenAmLegs(spot, context, "BULLISH", null, out var liftLegs, out var liftAt))
                {
                    yield return new StrategyCandidate(
                        lift.Strategy,
                        stock.Name,
                        "BULLISH",
                        79m,
                        lift.Evidence,
                        liftLegs,
                        lift.StopLoss,
                        lift.Target,
                        risk,
                        reward,
                        liftAt);
                }
            }
        }
    }

    private static bool TryBuildTenAmLegs(
        MarketContextSnapshot spot,
        IReadOnlyList<MarketContextSnapshot> context,
        string direction,
        MarketContextSnapshot? indexFuture,
        out IReadOnlyList<StrategyLeg> legs,
        out DateTimeOffset dataTimestamp)
    {
        legs = [];
        dataTimestamp = spot.Quote.DataTimestamp;
        if (ZoneBoardOptionLegSelector.TryBuildSingleLongOptionLeg(spot, direction, context, out var option)
            && option is not null)
        {
            legs = [option];
            dataTimestamp = option.Quote?.DataTimestamp ?? dataTimestamp;
            return true;
        }

        if (indexFuture is not null && indexFuture.Last > 0)
        {
            var side = direction == "BULLISH" ? "BUY" : "SELL";
            legs =
            [
                new StrategyLeg(
                    indexFuture.Symbol,
                    spot.Symbol,
                    "FUTURE",
                    side,
                    1,
                    indexFuture.Last,
                    Quote: indexFuture.Quote),
            ];
            dataTimestamp = indexFuture.Quote.DataTimestamp;
            return true;
        }

        if (!IndexUniverse.IsIndex(spot.Symbol) && spot.Last > 0)
        {
            var side = direction == "BULLISH" ? "BUY" : "SELL";
            legs =
            [
                new StrategyLeg(spot.Symbol, spot.Symbol, "STOCK", side, 1, spot.Last, Quote: spot.Quote),
            ];
            return true;
        }

        return false;
    }

    private static decimal ResolveDayOpenFromChart(ChartResponse chart)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist).DateTime);
        var first = chart.Candles
            .Where(b => DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(b.Time), ist).DateTime) == today)
            .OrderBy(b => b.Time)
            .FirstOrDefault();
        return first?.Open ?? 0;
    }
}
