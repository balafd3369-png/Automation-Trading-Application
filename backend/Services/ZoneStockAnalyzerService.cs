using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class ZoneStockAnalyzerService
{
    private readonly MockMarketService _market;
    private readonly ZoneAnalyzerSignalStore _signals;

    public ZoneStockAnalyzerService(MockMarketService market, ZoneAnalyzerSignalStore signals)
    {
        _market = market;
        _signals = signals;
    }

    public ZoneStockAnalyzerResponse Analyze()
    {
        var board = _market.Snapshot();
        var response = ZoneStockAnalyzerBuilder.Build(board);
        _signals.RecordScan(response);
        return response;
    }
}
