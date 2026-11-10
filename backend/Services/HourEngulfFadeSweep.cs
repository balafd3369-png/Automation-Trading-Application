using TradeWithBeast.Broker;

namespace TradeWithBeast.Services;

public static class HourEngulfFadeSweep
{
    public static async Task<IReadOnlyList<HourEngulfSweepRow>> RunAsync(
        Func<IReadOnlyList<string>, string, bool, decimal, Task<HourEngulfFadeBacktestReport>> runBacktest,
        int lookbackDays,
        decimal stockNotionalInr,
        CancellationToken ct = default)
    {
        var rows = new List<HourEngulfSweepRow>();
        var stockUniverses = new (string Key, IReadOnlyList<string> Symbols)[]
        {
            ("nifty50", IndexUniverse.Nifty50),
            ("banknifty_stocks", IndexUniverse.BankNifty),
            ("finnifty_stocks", IndexUniverse.FinNifty),
            ("sensex30", IndexUniverse.Sensex),
            ("all_equity_union", IndexUniverse.Equities),
        };

        foreach (var (key, symbols) in stockUniverses)
        {
            foreach (var filtered in new[] { true, false })
            {
                foreach (var mode in new[]
                         { HourEngulfFadeBacktest.ModeFadeLongStockNotional, HourEngulfFadeBacktest.ModeFadeLongStock })
                {
                    ct.ThrowIfCancellationRequested();
                    var r = await runBacktest(symbols, mode, filtered, stockNotionalInr);
                    rows.Add(ToRow(key, r));
                }
            }
        }

        foreach (var filtered in new[] { true, false })
        {
            ct.ThrowIfCancellationRequested();
            var r = await runBacktest(
                HourEngulfFadeBacktest.IndexFuturesUnderlyings,
                HourEngulfFadeBacktest.ModeFadeLongIndexFuture,
                filtered,
                stockNotionalInr);
            rows.Add(ToRow("index_futures", r));
        }

        return rows.OrderByDescending(x => x.TotalInrPnl).ToList();
    }

    private static HourEngulfSweepRow ToRow(string universe, HourEngulfFadeBacktestReport r) =>
        new(
            universe,
            r.Mode,
            r.FailureFiltersApplied,
            r.TradeCount,
            r.TradesPerMonth,
            r.WinRatePercent,
            r.TotalInrPnl,
            r.ExpectancyInrPerTrade,
            r.AvgWinInr,
            r.AvgLossInr,
            r.MaxLossInr,
            r.StockNotionalInr);
}

public sealed record HourEngulfSweepRow(
    string Universe,
    string Mode,
    bool FiltersApplied,
    int TradeCount,
    decimal TradesPerMonth,
    decimal WinRatePercent,
    decimal TotalInrPnl,
    decimal ExpectancyInrPerTrade,
    decimal AvgWinInr,
    decimal AvgLossInr,
    decimal MaxLossInr,
    decimal StockNotionalInr);
