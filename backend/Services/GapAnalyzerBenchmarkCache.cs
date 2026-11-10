using TradeWithBeast.Broker;

namespace TradeWithBeast.Services;

/// <summary>Caches Nifty 50 gap-study backtests for Analyzer UI (refreshed periodically).</summary>
public sealed class GapAnalyzerBenchmarkCache
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(45);
    private readonly ILogger<GapAnalyzerBenchmarkCache> _log;

    private GapBenchmarkSnapshot _snapshot = GapBenchmarkSnapshot.Fallback;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task? _refreshTask;

    public GapAnalyzerBenchmarkCache(ILogger<GapAnalyzerBenchmarkCache> log) => _log = log;

    public GapBenchmarkSnapshot Snapshot => _snapshot;

    public void KickRefreshIfStale(MockMarketService market)
    {
        if (DateTimeOffset.UtcNow - Snapshot.RefreshedAt < RefreshInterval)
        {
            return;
        }

        _refreshTask ??= RefreshLoopAsync(market);
    }

    private async Task RefreshLoopAsync(MockMarketService market)
    {
        try
        {
            await RefreshAsync(market, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Gap benchmark refresh failed");
        }
        finally
        {
            _refreshTask = null;
        }
    }

    public async Task RefreshAsync(MockMarketService market, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow - _snapshot.RefreshedAt < RefreshInterval)
            {
                return;
            }

            const int lookback = 120;
            var results = new List<GapStudySymbolResult>();
            foreach (var sym in IndexUniverse.Nifty50)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await market.EnsureSymbolAsync(sym, ct);
                    var chart = await market.ChartForBacktestAsync(sym, ZoneBoardPattern.ChartInterval, lookback, ct);
                    if (chart?.Candles is { Count: >= 100 })
                    {
                        results.Add(GapDayStudy.Analyze(sym, chart.Candles));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogDebug(ex, "Gap benchmark skip {Symbol}", sym);
                }
            }

            var report = GapDayStudy.Aggregate("nifty50", results);
            var bySymbol = new Dictionary<string, GapClueStats>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in results)
            {
                if (r.Clues.TryGetValue(GapDayStudy.ClueFailedRallyGapDown, out var stats))
                {
                    bySymbol[r.Symbol] = stats;
                }
            }

            var featured = report.Clues.FirstOrDefault(c => c.ClueId == GapDayStudy.ClueFailedRallyGapDown)
                ?? report.Clues.OrderByDescending(c => c.TradeWinRatePercent).FirstOrDefault();

            _snapshot = new GapBenchmarkSnapshot(
                DateTimeOffset.UtcNow,
                lookback,
                featured,
                report,
                bySymbol,
                report.Verdict);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed record GapBenchmarkSnapshot(
    DateTimeOffset RefreshedAt,
    int LookbackDays,
    GapStudyClueSummary? FeaturedClue,
    GapStudyUniverseReport? UniverseReport,
    IReadOnlyDictionary<string, GapClueStats> FailedRallyBySymbol,
    string Verdict)
{
    public static GapBenchmarkSnapshot Fallback { get; } = new(
        DateTimeOffset.MinValue,
        120,
        null,
        null,
        new Dictionary<string, GapClueStats>(),
        "Backtest cache loading — using live gap scan; universe stats refresh in background.");
}
