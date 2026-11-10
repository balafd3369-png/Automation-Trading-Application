using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class GapAnalyzerService
{
    private readonly MockMarketService _market;
    private readonly GapAnalyzerBenchmarkCache _benchmarks;

    public GapAnalyzerService(MockMarketService market, GapAnalyzerBenchmarkCache benchmarks)
    {
        _market = market;
        _benchmarks = benchmarks;
    }

    public GapAnalyzerResponse Analyze()
    {
        _benchmarks.KickRefreshIfStale(_market);
        var board = _market.Snapshot();
        var reversal = StockReversalPatternScanner.Scan(board);
        var snapshot = _benchmarks.Snapshot;
        if (snapshot.UniverseReport is null)
        {
            _ = _benchmarks.RefreshAsync(_market, CancellationToken.None);
        }

        return GapAnalyzerBuilder.Build(board, reversal, snapshot);
    }
}
