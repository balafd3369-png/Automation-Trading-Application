using System.Globalization;
using System.Text;
using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Overnight fade after 1h green→2 red: long from pattern-day close to next open (stocks 1 qty or index futures lot).
/// </summary>
public static class HourEngulfFadeBacktest
{
    public const string ModeFadeLongStock = "FADE_LONG_STOCK";
    public const string ModeFadeLongStockNotional = "FADE_LONG_STOCK_NOTIONAL";
    public const string ModeFadeLongIndexFuture = "FADE_LONG_INDEX_FUTURE";
    public const string ModeShortGapStock = "SHORT_GAP_STOCK";

    public const decimal DefaultStockNotionalInr = 50_000m;

    public static readonly string[] IndexFuturesUnderlyings = ["NIFTY", "BANKNIFTY", "FINNIFTY", "SENSEX"];

    public static HourEngulfFadeBacktestReport Run(
        IReadOnlyList<string> symbols,
        Func<string, IReadOnlyList<ChartBar>?> loadBars,
        int lookbackDays,
        string mode,
        bool applyFailureFilters,
        decimal stockNotionalInr = DefaultStockNotionalInr)
    {
        var trades = new List<HourEngulfFadeTrade>();
        foreach (var sym in symbols)
        {
            var bars = loadBars(sym);
            if (bars is null || bars.Count < 200)
            {
                continue;
            }

            trades.AddRange(BuildTradesForSymbol(sym, bars, mode, applyFailureFilters, stockNotionalInr));
        }

        return Summarize(trades, lookbackDays, mode, applyFailureFilters, stockNotionalInr);
    }

    private static IEnumerable<HourEngulfFadeTrade> BuildTradesForSymbol(
        string symbol,
        IReadOnlyList<ChartBar> bars5m,
        string mode,
        bool applyFailureFilters,
        decimal stockNotionalInr)
    {
        var events = HourEngulfFailureProfiler.CollectEvents(symbol, bars5m, HourEngulfGapStudy.ClueGreenCoverGapDown);
        var byDay = events
            .GroupBy(e => e.PatternDate)
            .OrderBy(g => g.Key);
        var rows = new List<HourEngulfFadeTrade>();

        foreach (var g in byDay)
        {
            var e = g.OrderByDescending(x => x.PatternEndHourIst).First();
            if (applyFailureFilters && ShouldSkip(e))
            {
                continue;
            }

            var ohlc = ResolveOhlc(bars5m, e.PatternDate);
            if (ohlc is null)
            {
                continue;
            }

            var (_, todayClose, nextOpen) = ohlc.Value;
            if (todayClose <= 0 || nextOpen <= 0)
            {
                continue;
            }

            var gapPct = (nextOpen - todayClose) / todayClose * 100m;
            var lot = IndexUniverse.IsIndex(symbol) ? PortfolioRiskManager.LotSize(symbol) : 1;
            var qty = mode switch
            {
                ModeFadeLongIndexFuture => lot,
                ModeFadeLongStockNotional => Math.Max(1, (int)Math.Floor(stockNotionalInr / todayClose)),
                _ => 1,
            };

            decimal pointsPnl;
            decimal inrPnl;
            bool win;
            string side;
            if (mode == ModeShortGapStock)
            {
                side = "SHORT";
                pointsPnl = todayClose - nextOpen;
                inrPnl = pointsPnl * qty;
                win = gapPct <= -HourEngulfGapStudy.MinGapPercent;
            }
            else
            {
                side = "LONG";
                pointsPnl = nextOpen - todayClose;
                inrPnl = pointsPnl * qty;
                win = gapPct > -HourEngulfGapStudy.MinGapPercent;
            }

            rows.Add(new HourEngulfFadeTrade(
                symbol,
                e.PatternDate,
                side,
                todayClose,
                nextOpen,
                gapPct,
                pointsPnl,
                qty,
                inrPnl,
                win,
                e.PatternCompletesPm,
                e.PriorCloseBelowS1,
                e.PriorFailedRally,
                e.DayOfWeek));
        }

        return rows;
    }

    private static bool ShouldSkip(HourEngulfFailureProfiler.HourEngulfEvent e) =>
        e.PriorCloseBelowS1
        || e.PriorFailedRally
        || e.DayOfWeek == nameof(DayOfWeek.Friday);

    private static (DateOnly Day, decimal Close, decimal NextOpen)? ResolveOhlc(IReadOnlyList<ChartBar> bars, DateOnly patternDay)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var daily = bars
            .GroupBy(b => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(b.Time), ist).DateTime))
            .OrderBy(x => x.Key)
            .Select(g =>
            {
                var day = g.OrderBy(x => x.Time).ToList();
                return (Date: g.Key, Open: day[0].Open, Close: day[^1].Close);
            })
            .ToList();

        for (var i = 0; i < daily.Count - 1; i++)
        {
            if (daily[i].Date != patternDay)
            {
                continue;
            }

            return (patternDay, daily[i].Close, daily[i + 1].Open);
        }

        return null;
    }

    private static HourEngulfFadeBacktestReport Summarize(
        List<HourEngulfFadeTrade> trades,
        int lookbackDays,
        string mode,
        bool filtered,
        decimal stockNotionalInr)
    {
        var closed = trades.Where(t => t.InrPnl != 0 || t.PointsPnl != 0).ToList();
        var wins = closed.Where(t => t.IsWin).ToList();
        var losses = closed.Where(t => !t.IsWin).ToList();
        var totalInr = closed.Sum(t => t.InrPnl);
        var avgWinInr = wins.Count > 0 ? wins.Average(t => t.InrPnl) : 0m;
        var avgLossInr = losses.Count > 0 ? losses.Average(t => t.InrPnl) : 0m;
        var avgWinPts = wins.Count > 0 ? wins.Average(t => t.PointsPnl) : 0m;
        var avgLossPts = losses.Count > 0 ? losses.Average(t => t.PointsPnl) : 0m;
        var winRate = closed.Count > 0 ? (decimal)wins.Count / closed.Count * 100m : 0m;
        var expectancy = closed.Count > 0 ? totalInr / closed.Count : 0m;

        var maxLoss = closed.Count > 0 ? closed.Min(t => t.InrPnl) : 0m;
        var maxWin = closed.Count > 0 ? closed.Max(t => t.InrPnl) : 0m;

        var months = lookbackDays / 30m;
        var tradesPerMonth = months > 0 ? closed.Count / months : 0m;

        var filterNote = filtered
            ? "Filters ON: skip Friday; skip prior close below S1; skip prior failed rally (open≥B1 then close<B1)."
            : "Filters OFF: every pattern day counts (more trades, more false fades).";
        var sizingNote = mode switch
        {
            ModeFadeLongIndexFuture => "One index futures lot per signal (NIFTY/BANKNIFTY/FINNIFTY/SENSEX).",
            ModeFadeLongStockNotional => $"Stock qty ≈ ₹{stockNotionalInr:N0} / close price per name.",
            _ => "One cash share per signal (sanity check only — use NOTIONAL mode for rupees).",
        };

        var execution = new HourEngulfExecutionGuide(
            "1h green → two red cover (2nd red close below green open) on pattern day.",
            "Pattern completes when the 3rd hour candle closes (scan each hour after 10:15 IST).",
            "Enter LONG at pattern-day close (~15:30); exit at next session open (~9:15).",
            filterNote,
            sizingNote);

        return new HourEngulfFadeBacktestReport(
            lookbackDays,
            mode,
            filtered,
            stockNotionalInr,
            tradesPerMonth,
            closed.Count,
            wins.Count,
            losses.Count,
            winRate,
            totalInr,
            avgWinInr,
            avgLossInr,
            avgWinPts,
            avgLossPts,
            expectancy,
            maxWin,
            maxLoss,
            execution,
            closed.OrderByDescending(t => t.PatternDate).ToList());
    }

    public static string ToCsv(IReadOnlyList<HourEngulfFadeTrade> trades)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "Symbol,PatternDate,Side,EntryClose,ExitNextOpen,GapPercent,PointsPnl,Quantity,InrPnl,Win,PmPattern,PriorBelowS1,PriorFailedRally,DayOfWeek");
        foreach (var t in trades.OrderBy(t => t.PatternDate).ThenBy(t => t.Symbol))
        {
            sb.Append(CultureInfo.InvariantCulture, $"{t.Symbol},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.PatternDate:yyyy-MM-dd},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.Side},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.EntryClose:0.##},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.ExitNextOpen:0.##},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.GapPercent:0.####},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.PointsPnl:0.##},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.Quantity},");
            sb.Append(CultureInfo.InvariantCulture, $"{t.InrPnl:0.##},");
            sb.Append(CultureInfo.InvariantCulture, $"{(t.IsWin ? 1 : 0)},");
            sb.Append(CultureInfo.InvariantCulture, $"{(t.PatternCompletesPm ? 1 : 0)},");
            sb.Append(CultureInfo.InvariantCulture, $"{(t.PriorCloseBelowS1 ? 1 : 0)},");
            sb.Append(CultureInfo.InvariantCulture, $"{(t.PriorFailedRally ? 1 : 0)},");
            sb.AppendLine(t.DayOfWeek);
        }

        return sb.ToString();
    }
}

public sealed record HourEngulfFadeTrade(
    string Symbol,
    DateOnly PatternDate,
    string Side,
    decimal EntryClose,
    decimal ExitNextOpen,
    decimal GapPercent,
    decimal PointsPnl,
    int Quantity,
    decimal InrPnl,
    bool IsWin,
    bool PatternCompletesPm,
    bool PriorCloseBelowS1,
    bool PriorFailedRally,
    string DayOfWeek);

public sealed record HourEngulfExecutionGuide(
    string Pattern,
    string SignalTime,
    string EntryExit,
    string Filters,
    string SizingNote);

public sealed record HourEngulfFadeBacktestReport(
    int LookbackDays,
    string Mode,
    bool FailureFiltersApplied,
    decimal StockNotionalInr,
    decimal TradesPerMonth,
    int TradeCount,
    int Wins,
    int Losses,
    decimal WinRatePercent,
    decimal TotalInrPnl,
    decimal AvgWinInr,
    decimal AvgLossInr,
    decimal AvgWinPoints,
    decimal AvgLossPoints,
    decimal ExpectancyInrPerTrade,
    decimal MaxWinInr,
    decimal MaxLossInr,
    HourEngulfExecutionGuide Execution,
    IReadOnlyList<HourEngulfFadeTrade> TradeLog);
