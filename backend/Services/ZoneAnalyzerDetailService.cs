using Microsoft.Extensions.Caching.Memory;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class ZoneAnalyzerDetailService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
    private readonly MockMarketService _market;
    private readonly IMemoryCache _cache;
    private readonly ZoneAnalyzerSignalStore _signals;

    public ZoneAnalyzerDetailService(
        MockMarketService market,
        IMemoryCache cache,
        ZoneAnalyzerSignalStore signals)
    {
        _market = market;
        _cache = cache;
        _signals = signals;
    }

    public async Task<ZoneAnalyzerPatternDetailResponse?> GetDetailAsync(
        string name,
        string patternId,
        int lookbackDays = 90,
        CancellationToken ct = default)
    {
        var board = _market.Snapshot();
        if (board.Symbols.All(x => !x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        lookbackDays = Math.Clamp(lookbackDays, 7, 365);
        var cacheKey = $"zone-analyzer-detail:{name}:{patternId}:{lookbackDays}";
        if (_cache.TryGetValue(cacheKey, out ZoneAnalyzerPatternDetailResponse? cached) && cached is not null)
        {
            return cached;
        }

        var chart = await _market.ChartForBacktestAsync(name, ZoneBoardPattern.ChartInterval, lookbackDays, ct);
        if (chart is null)
        {
            return null;
        }

        var candles = chart.Candles;
        var (winRate, samples, wins, losses, avgReward, avgRisk, qualifies) = BacktestForPattern(name, patternId, candles);
        var (lastWins, lastLosses) = Last20Trades(name, patternId, candles);

        var rr = avgRisk > 0 ? avgReward / avgRisk : (decimal?)null;
        var adaptive = _signals.StatsForPattern(patternId);
        var confidenceLabel = qualifies ? "Strong" : samples >= ZoneBoardPattern.MinBacktestSamples ? "Moderate" : "Thin history";

        var detail = new ZoneAnalyzerPatternDetailResponse(
            name,
            patternId,
            lookbackDays,
            winRate,
            samples,
            wins,
            losses,
            avgReward,
            avgRisk,
            rr,
            lastWins,
            lastLosses,
            confidenceLabel,
            qualifies,
            adaptive.SignalsLogged,
            adaptive.WinRatePercent);

        _cache.Set(cacheKey, detail, CacheTtl);
        return detail;
    }

    private static (decimal Wr, int Samples, int Wins, int Losses, decimal AvgRew, decimal AvgRisk, bool Qualifies)
        BacktestForPattern(string name, string patternId, IReadOnlyList<ChartBar> candles)
    {
        if (patternId.Equals(ZoneRejectionPattern.B1RejectionToS2, StringComparison.OrdinalIgnoreCase))
        {
            var r = ZoneBoardPattern.BacktestB1RejectionShort(name, candles);
            var q = r.Samples >= ZoneRejectionPattern.MinBacktestSamples
                && r.WinRatePercent >= ZoneRejectionPattern.MinWinRateToRecommend;
            return (r.WinRatePercent, r.Samples, r.Wins, r.Losses, r.AvgRewardPoints, r.AvgRiskPoints, q);
        }

        ZoneBoardPattern.ZoneBoardBacktestResult result = patternId switch
        {
            _ when patternId.Equals(ZoneBoardPattern.B1BreakLong, StringComparison.OrdinalIgnoreCase)
                => ZoneBoardPattern.BacktestB1BreakLong(name, candles),
            _ when patternId.Equals(ZoneBoardPattern.S1BreakShort, StringComparison.OrdinalIgnoreCase)
                => ZoneBoardPattern.BacktestS1BreakShort(name, candles),
            _ when patternId.Equals(ZoneBoardPattern.S2BounceLong, StringComparison.OrdinalIgnoreCase)
                => ZoneBoardPattern.BacktestS2BounceLong(name, candles),
            _ => ZoneBoardPattern.BacktestB1BreakLong(name, candles),
        };

        return (
            result.WinRatePercent,
            result.Samples,
            result.Wins,
            result.Losses,
            result.AvgRewardPoints,
            result.AvgRiskPoints,
            ZoneBoardPattern.Qualifies(result));
    }

    private static (int Wins, int Losses) Last20Trades(
        string name,
        string patternId,
        IReadOnlyList<ChartBar> candles)
    {
        IReadOnlyList<ZoneBoardPattern.ZoneBoardTradeJournalEntry> journal = patternId switch
        {
            _ when patternId.Equals(ZoneBoardPattern.B1BreakLong, StringComparison.OrdinalIgnoreCase)
                => ZoneBoardPattern.JournalB1BreakLong(name, candles),
            _ when patternId.Equals(ZoneBoardPattern.S1BreakShort, StringComparison.OrdinalIgnoreCase)
                => ZoneBoardPattern.JournalS1BreakShort(name, candles),
            _ => [],
        };

        var last = journal.OrderByDescending(t => t.EntryTime).Take(20).ToList();
        var wins = last.Count(t => t.Result.Equals("WIN", StringComparison.OrdinalIgnoreCase));
        var losses = last.Count(t => t.Result.Equals("LOSS", StringComparison.OrdinalIgnoreCase));
        return (wins, losses);
    }
}
