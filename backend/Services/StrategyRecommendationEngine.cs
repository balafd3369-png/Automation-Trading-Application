using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class StrategyRecommendationEngine(
    MockMarketService market,
    ZoneSessionTracker zoneSessions,
    TradeHistoryStore history)
{
    private readonly object _backtestGate = new();
    private readonly Dictionary<string, (DateOnly Day, ZoneRejectionBacktestResult Result)> _backtests = new(StringComparer.OrdinalIgnoreCase);

    public StrategyFeedResponse BuildFeed(CancellationToken ct)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var now = DateTimeOffset.UtcNow;
        var istDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, ist).DateTime);
        var board = market.Snapshot();
        var live = new List<StrategyLiveRecommendation>();

        foreach (var symbol in board.Symbols)
        {
            var isIndex = IndexUniverse.IsIndex(symbol.Name);
            if (!isIndex && !IndexUniverse.Nifty50.Contains(symbol.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();
            ChartResponse? chart;
            try
            {
                chart = market.ChartAsync(symbol.Name, TenAmIntradayPattern.ChartInterval, ct)
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

            var dayOpen = symbol.TodayOpen > 0 ? symbol.TodayOpen : chart.Candles[0].Open;
            if (dayOpen <= 0)
            {
                continue;
            }

            if (TenAmIntradayPattern.TryBuildHighFade(symbol.Name, chart.Candles, dayOpen, now, out var fade)
                && fade is not null)
            {
                live.Add(ToRecommendation(fade, symbol.Name, isIndex, now, board));
            }

            if (TenAmIntradayPattern.TryBuildLowBreak(symbol.Name, chart.Candles, dayOpen, now, out var lift)
                && lift is not null)
            {
                live.Add(ToRecommendation(lift, symbol.Name, isIndex, now, board));
            }

            if (isIndex && zoneSessions.HasPendingRejection(symbol.Name, istDay))
            {
                if (TryBuildB1Recommendation(symbol, chart, istDay, now, out var b1Rec) && b1Rec is not null)
                {
                    live.Add(b1Rec);
                }
            }
        }

        live = live
            .OrderByDescending(x => x.DetectedAt)
            .ThenBy(x => x.Symbol, StringComparer.Ordinal)
            .ToList();

        var stats = history.QueryStrategyTrackRecord(now);
        var catalog = StrategyCatalog.All();
        var monthly = catalog.Select(entry =>
        {
            var row = stats.ByStrategy.FirstOrDefault(x => x.StrategyId == entry.Id);
            return new StrategyMonthlyStat(
                entry.Id,
                entry.Name,
                row?.Trades ?? 0,
                row?.ClosedTrades ?? 0,
                row?.WinningTrades ?? 0,
                row?.RealizedPnl ?? 0,
                row?.PeakLiveCapital ?? 0,
                row?.WinRatePercent ?? 0);
        }).ToList();

        return new(live, catalog, monthly, now);
    }

    private bool TryBuildB1Recommendation(
        SymbolSnapshot symbol,
        ChartResponse chart,
        DateOnly istDay,
        DateTimeOffset now,
        out StrategyLiveRecommendation? recommendation)
    {
        recommendation = null;
        var b1 = symbol.B1;
        var s2 = symbol.S2;
        if (b1 <= 0 || s2 <= 0)
        {
            return false;
        }

        var backtest = LoadBacktest(symbol.Name, istDay, b1, s2, chart);
        if (!ZoneRejectionPattern.QualifiesForUserRecommendation(backtest))
        {
            return false;
        }

        var entry = symbol.Last;
        var stop = Math.Max(b1, entry) * 1.0015m + TenAmIntradayPattern.StopBufferPoints;
        var riskPts = stop - entry;
        var rewardPts = entry - s2;
        if (riskPts <= 0 || rewardPts <= 0)
        {
            return false;
        }

        var riskInr = riskPts;
        var rewardInr = rewardPts;
        recommendation = new StrategyLiveRecommendation(
            $"{ZoneRejectionPattern.B1RejectionToS2}:{symbol.Name}:live",
            StrategyCatalog.StockB1RejectionId,
            StrategyCatalog.Find(StrategyCatalog.StockB1RejectionId)?.Name ?? "Stock B1 rejection → S2",
            symbol.Name,
            "BEARISH",
            $"{symbol.Name} — B1 rejection live now. Paper trade eligible.",
            "LIVE_NOW",
            entry,
            stop,
            s2,
            riskPts,
            rewardPts,
            riskInr,
            rewardInr,
            rewardInr / Math.Max(0.01m, riskInr),
            [
                $"B1 touch/reject near {b1:0.##}",
                $"Backtest {backtest.WinRatePercent:0.#}% over {backtest.Samples} samples (15m)",
                $"Target S2 {s2:0.##}",
            ],
            [$"If target S2 hits → profit about ₹{rewardInr:0.##} per share (paper sizing)."],
            [$"If stop hits → loss about ₹{riskInr:0.##} per share."],
            0,
            now,
            true);
        return true;
    }

    private StrategyLiveRecommendation ToRecommendation(
        TenAmSignal signal,
        string symbol,
        bool isIndex,
        DateTimeOffset now,
        BoardResponse board)
    {
        var catalogId = MapStrategyCode(signal.Strategy);
        var catalog = StrategyCatalog.Find(catalogId);
        var lot = isIndex ? PortfolioRiskManager.LotSize(symbol) : 1;
        var entry = signal.Entry;
        var riskPts = Math.Abs(signal.StopLoss - entry);
        var rewardPts = Math.Abs(entry - signal.Target);
        var riskInr = riskPts * lot;
        var rewardInr = rewardPts * lot;
        var dir = signal.Strategy == TenAmIntradayPattern.HighFadeStrategy ? "BEARISH" : "BULLISH";
        var headline = $"{symbol} — {catalog?.Name ?? signal.Strategy} is active on 15m. You can take this paper trade now.";

        return new StrategyLiveRecommendation(
            $"{signal.Strategy}:{symbol}:{signal.SignalBarTime}",
            catalogId,
            catalog?.Name ?? signal.Strategy,
            symbol,
            dir,
            headline,
            "LIVE_NOW",
            entry,
            signal.StopLoss,
            signal.Target,
            riskPts,
            rewardPts,
            riskInr,
            rewardInr,
            rewardInr / Math.Max(0.01m, riskInr),
            signal.Evidence.ToList(),
            [$"If target {signal.Target:0.##} hits → profit about ₹{rewardInr:0.##} (lot {lot})."],
            [$"If stop {signal.StopLoss:0.##} hits → loss about ₹{riskInr:0.##} (lot {lot})."],
            signal.SignalBarTime,
            now,
            true);
    }

    private ZoneRejectionBacktestResult LoadBacktest(string symbol, DateOnly istDay, decimal b1, decimal s2, ChartResponse chart)
    {
        lock (_backtestGate)
        {
            if (_backtests.TryGetValue(symbol, out var cached) && cached.Day == istDay)
            {
                return cached.Result;
            }
        }

        var board = ZoneBoardPattern.BacktestB1RejectionShort(symbol, chart.Candles);
        var result = ToRejectionResult(board);
        lock (_backtestGate)
        {
            _backtests[symbol] = (istDay, result);
        }

        return result;
    }

    private static ZoneRejectionBacktestResult ToRejectionResult(ZoneBoardPattern.ZoneBoardBacktestResult board) =>
        new(
            board.Symbol,
            board.Pattern,
            board.Samples,
            board.Wins,
            board.Losses,
            board.WinRatePercent,
            board.AvgRewardPoints,
            board.AvgRiskPoints);

    public static string MapStrategyCode(string strategyOrCatalogId) => strategyOrCatalogId switch
    {
        StrategyCatalog.TenAmHighFadeId or TenAmIntradayPattern.HighFadeStrategy => StrategyCatalog.TenAmHighFadeId,
        StrategyCatalog.TenAmLowBreakToDayOpenId or TenAmIntradayPattern.LowBreakStrategy => StrategyCatalog.TenAmLowBreakToDayOpenId,
        StrategyCatalog.StockB1RejectionId or ZoneRejectionPattern.B1RejectionToS2 => StrategyCatalog.StockB1RejectionId,
        StrategyCatalog.ZoneB1BreakId or ZoneBoardPattern.B1BreakLong => StrategyCatalog.ZoneB1BreakId,
        StrategyCatalog.ZoneS1BreakId or ZoneBoardPattern.S1BreakShort => StrategyCatalog.ZoneS1BreakId,
        StrategyCatalog.ZoneS2BounceId or ZoneBoardPattern.S2BounceLong => StrategyCatalog.ZoneS2BounceId,
        _ => strategyOrCatalogId,
    };

    public static string StrategyCodeFromCatalog(string catalogId) => catalogId switch
    {
        StrategyCatalog.TenAmHighFadeId => TenAmIntradayPattern.HighFadeStrategy,
        StrategyCatalog.TenAmLowBreakToDayOpenId => TenAmIntradayPattern.LowBreakStrategy,
        StrategyCatalog.StockB1RejectionId => ZoneRejectionPattern.B1RejectionToS2,
        StrategyCatalog.ZoneB1BreakId => ZoneBoardPattern.B1BreakLong,
        StrategyCatalog.ZoneS1BreakId => ZoneBoardPattern.S1BreakShort,
        StrategyCatalog.ZoneS2BounceId => ZoneBoardPattern.S2BounceLong,
        _ => catalogId,
    };
}
