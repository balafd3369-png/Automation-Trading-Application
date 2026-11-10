using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Historical study: prior-session zone context → next-day gap and continuation (stocks).
/// </summary>
public static class GapDayStudy
{
    public const decimal MinGapPercent = 0.35m;
    public const int MinSamplesPerClue = 8;

    public const string ClueDistributionGapDown = "GAP_DOWN_AFTER_DISTRIBUTION";
    public const string ClueWeakCloseGapDown = "GAP_DOWN_AFTER_CLOSE_BELOW_S1";
    public const string ClueStrongCloseGapUp = "GAP_UP_AFTER_CLOSE_ABOVE_B1";
    public const string ClueFailedRallyGapDown = "GAP_DOWN_AFTER_FAILED_RALLY";

    public static GapStudySymbolResult Analyze(string symbol, IReadOnlyList<ChartBar> bars)
    {
        var ist = IndiaTz();
        var daily = BuildDailyOhlc(bars, ist);
        if (daily.Count < 4)
        {
            return GapStudySymbolResult.Empty(symbol);
        }

        var zones = ZoneBoardPattern.BuildDailyZones(bars, ist);
        var clues = new Dictionary<string, ClueBucket>
        {
            [ClueDistributionGapDown] = new(),
            [ClueWeakCloseGapDown] = new(),
            [ClueStrongCloseGapUp] = new(),
            [ClueFailedRallyGapDown] = new(),
        };

        var sessionBars = bars.GroupBy(b => IstDate(ist, b.Time)).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Time).ToList());

        for (var i = 2; i < daily.Count; i++)
        {
            var prior = daily[i - 1];
            var today = daily[i];
            if (!zones.TryGetValue(today.Date, out var zToday))
            {
                continue;
            }

            if (!zones.TryGetValue(prior.Date, out var zPrior))
            {
                continue;
            }

            var gapPct = prior.Close > 0 ? (today.Open - prior.Close) / prior.Close * 100m : 0m;
            var tol = ZoneBoardPattern.TouchTolerance;
            var dist = prior.Open >= zPrior.B1 * (1m - tol) && prior.Close <= zPrior.S1 * (1m + tol);
            var weak = prior.Close < zPrior.S1 * (1m - tol);
            var strong = prior.Close >= zPrior.B1 * (1m - tol);
            var failedRally = prior.Open >= zPrior.B1 * (1m - tol)
                && prior.Close < prior.Open
                && prior.Close < zPrior.B1;

            if (dist && gapPct <= -MinGapPercent)
            {
                RecordClue(clues[ClueDistributionGapDown], gapPct, prior, today, zToday, sessionBars, today.Date);
            }

            if (weak && gapPct <= -MinGapPercent)
            {
                RecordClue(clues[ClueWeakCloseGapDown], gapPct, prior, today, zToday, sessionBars, today.Date);
            }

            if (strong && gapPct >= MinGapPercent)
            {
                RecordClue(clues[ClueStrongCloseGapUp], gapPct, prior, today, zToday, sessionBars, today.Date, bullish: true);
            }

            if (failedRally && gapPct <= -MinGapPercent)
            {
                RecordClue(clues[ClueFailedRallyGapDown], gapPct, prior, today, zToday, sessionBars, today.Date);
            }
        }

        return new GapStudySymbolResult(
            symbol,
            daily.Count,
            clues.ToDictionary(kv => kv.Key, kv => kv.Value.ToStats()));
    }

    public static GapStudyUniverseReport Aggregate(string universe, IReadOnlyList<GapStudySymbolResult> perSymbol) =>
        GapStudyReportBuilder.Build(universe, perSymbol);

    private static void RecordClue(
        ClueBucket bucket,
        decimal gapPct,
        DailyOhlc prior,
        DailyOhlc today,
        ZoneBoardPattern.ZoneBoardLevels zToday,
        Dictionary<DateOnly, List<ChartBar>> sessionBars,
        DateOnly todayDate,
        bool bullish = false)
    {
        bucket.GapSum += gapPct;
        bucket.Events++;

        var gapDayBear = today.Close < today.Open;
        var gapDayBull = today.Close > today.Open;
        if (bullish ? gapDayBull : gapDayBear)
        {
            bucket.ContinuationDays++;
        }

        if (!sessionBars.TryGetValue(todayDate, out var dayBars) || dayBars.Count < 4)
        {
            return;
        }

        if (zToday.S2 >= zToday.S1 && !bullish)
        {
            return;
        }

        if (bullish && zToday.B2 <= zToday.B1)
        {
            return;
        }

        var entry = dayBars[0].Open > 0 ? dayBars[0].Open : dayBars[0].Close;
        decimal stop;
        decimal target;
        bool bearish;
        if (bullish)
        {
            stop = Math.Min(today.Low, zToday.S1) * (1m - ZoneBoardPattern.TouchTolerance);
            target = zToday.B2;
            bearish = false;
        }
        else
        {
            stop = Math.Max(today.High, entry) * (1m + ZoneBoardPattern.TouchTolerance);
            target = zToday.S2;
            bearish = true;
        }

        if (bearish && (target >= entry || stop <= entry))
        {
            return;
        }

        if (!bearish && (target <= entry || stop >= entry))
        {
            return;
        }

        var outcome = SimulateDay(dayBars, 1, entry, stop, target, bearish);
        bucket.TradeSamples++;
        if (outcome == true)
        {
            bucket.TradeWins++;
        }
        else if (outcome == false)
        {
            bucket.TradeLosses++;
        }
        else
        {
            var settle = dayBars[^1].Close;
            if (bearish)
            {
                if (settle <= target)
                {
                    bucket.TradeWins++;
                }
                else if (settle >= stop)
                {
                    bucket.TradeLosses++;
                }
                else
                {
                    bucket.TradeWins += settle < entry ? 1 : 0;
                    bucket.TradeLosses += settle >= entry ? 1 : 0;
                }
            }
            else
            {
                if (settle >= target)
                {
                    bucket.TradeWins++;
                }
                else if (settle <= stop)
                {
                    bucket.TradeLosses++;
                }
                else
                {
                    bucket.TradeWins += settle > entry ? 1 : 0;
                    bucket.TradeLosses += settle <= entry ? 1 : 0;
                }
            }
        }
    }

    private static bool? SimulateDay(
        IReadOnlyList<ChartBar> bars,
        int startIndex,
        decimal entry,
        decimal stop,
        decimal target,
        bool bearish)
    {
        for (var i = startIndex; i < bars.Count; i++)
        {
            var bar = bars[i];
            if (bearish)
            {
                if (bar.High >= stop)
                {
                    return false;
                }

                if (bar.Low <= target)
                {
                    return true;
                }
            }
            else
            {
                if (bar.Low <= stop)
                {
                    return false;
                }

                if (bar.High >= target)
                {
                    return true;
                }
            }
        }

        return null;
    }

    private static List<DailyOhlc> BuildDailyOhlc(IReadOnlyList<ChartBar> bars, TimeZoneInfo ist) =>
        bars
            .GroupBy(bar => IstDate(ist, bar.Time))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var day = g.OrderBy(b => b.Time).ToList();
                return new DailyOhlc(
                    g.Key,
                    day[0].Open,
                    day.Max(b => b.High),
                    day.Min(b => b.Low),
                    day[^1].Close);
            })
            .ToList();

    private static TimeZoneInfo IndiaTz() => TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    private static DateOnly IstDate(TimeZoneInfo ist, long unixSeconds) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), ist).DateTime);

    private sealed record DailyOhlc(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close);

    private sealed class ClueBucket
    {
        public int Events;
        public int ContinuationDays;
        public decimal GapSum;
        public int TradeSamples;
        public int TradeWins;
        public int TradeLosses;

        public GapClueStats ToStats()
        {
            var cont = Events > 0 ? (decimal)ContinuationDays / Events * 100m : 0m;
            var avgGap = Events > 0 ? GapSum / Events : 0m;
            var trades = TradeWins + TradeLosses;
            var win = trades > 0 ? (decimal)TradeWins / trades * 100m : 0m;
            return new GapClueStats(
                Events,
                ContinuationDays,
                cont,
                avgGap,
                TradeWins,
                TradeLosses,
                win,
                trades >= MinSamplesPerClue);
        }
    }
}

public sealed record GapClueStats(
    int Events,
    int ContinuationDays,
    decimal ContinuationRatePercent,
    decimal AvgGapPercent,
    int TradeWins,
    int TradeLosses,
    decimal TradeWinRatePercent,
    bool Qualifies);

public sealed record GapStudySymbolResult(
    string Symbol,
    int SessionDays,
    IReadOnlyDictionary<string, GapClueStats> Clues)
{
    public static GapStudySymbolResult Empty(string symbol) =>
        new(symbol, 0, new Dictionary<string, GapClueStats>());
}

public sealed record GapStudyClueSummary(
    string ClueId,
    string Title,
    string PriorSessionRule,
    string GapRule,
    string TradeRule,
    int TotalEvents,
    int SymbolsWithEvents,
    decimal AvgContinuationRatePercent,
    int TradeWins,
    int TradeLosses,
    decimal TradeWinRatePercent,
    bool Qualifies);

public sealed record GapStudyUniverseReport(
    string Universe,
    int SymbolsProcessed,
    int LookbackSessionDaysHint,
    IReadOnlyList<GapStudyClueSummary> Clues,
    string Verdict);

public static class GapStudyReportBuilder
{
    private static readonly IReadOnlyList<(string Id, string Title, string Prior, string Gap, string Trade)> Meta =
    [
        (
            GapDayStudy.ClueDistributionGapDown,
            "Distribution → next-day gap down",
            "Prior day: open ≥ B1 and close ≤ S1 (full gunshot / distribution).",
            "Today open gaps down ≥ 0.35% vs prior close.",
            "Short from today open, stop session high, target today S2 (5m sim)."),
        (
            GapDayStudy.ClueWeakCloseGapDown,
            "Weak close → gap down",
            "Prior day close below S1.",
            "Gap down ≥ 0.35%.",
            "Same short continuation to S2."),
        (
            GapDayStudy.ClueFailedRallyGapDown,
            "Failed rally → gap down",
            "Prior: open ≥ B1 but close below open and below B1.",
            "Gap down ≥ 0.35%.",
            "Same short continuation to S2."),
        (
            GapDayStudy.ClueStrongCloseGapUp,
            "Strong close → gap up",
            "Prior day close ≥ B1.",
            "Gap up ≥ 0.35%.",
            "Long from open, stop session low / S1, target B2."),
    ];

    public static IReadOnlyList<object> ClueDefinitions() =>
        Meta.Select(m => (object)new { m.Id, m.Title, m.Prior, m.Gap, m.Trade }).ToList();

    public static GapStudyUniverseReport Build(string universe, IReadOnlyList<GapStudySymbolResult> perSymbol)
    {
        var summaries = new List<GapStudyClueSummary>();
        foreach (var (id, title, prior, gap, trade) in Meta)
        {
            var events = 0;
            var symbols = 0;
            var contSum = 0m;
            var contN = 0;
            var wins = 0;
            var losses = 0;
            foreach (var sym in perSymbol)
            {
                if (!sym.Clues.TryGetValue(id, out var c) || c.Events == 0)
                {
                    continue;
                }

                symbols++;
                events += c.Events;
                if (c.Events > 0)
                {
                    contSum += c.ContinuationRatePercent;
                    contN++;
                }

                wins += c.TradeWins;
                losses += c.TradeLosses;
            }

            var trades = wins + losses;
            var winRate = trades > 0 ? (decimal)wins / trades * 100m : 0m;
            var avgCont = contN > 0 ? contSum / contN : 0m;
            summaries.Add(new GapStudyClueSummary(
                id,
                title,
                prior,
                gap,
                trade,
                events,
                symbols,
                avgCont,
                wins,
                losses,
                winRate,
                trades >= GapDayStudy.MinSamplesPerClue && winRate >= ZoneBoardPattern.MinWinRateToRecommend));
        }

        var best = summaries
            .Where(s => s.Qualifies)
            .OrderByDescending(s => s.TradeWinRatePercent)
            .FirstOrDefault();

        var verdict = best is null
            ? "No gap clue met minimum samples (8+) and 55% trade win rate in this window — use per-symbol Analyzer before adding rules."
            : $"Strongest verified clue: {best.Title} — {best.TradeWinRatePercent:F1}% wins ({best.TradeWins}W/{best.TradeLosses}L) across universe.";

        return new GapStudyUniverseReport(
            universe,
            perSymbol.Count,
            perSymbol.Count > 0 ? perSymbol.Max(s => s.SessionDays) : 0,
            summaries,
            verdict);
    }
}
