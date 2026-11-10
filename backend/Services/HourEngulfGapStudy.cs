using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// User hypothesis: 1h green hour, then two 1h reds covering it and closing below → next session gap down.
/// Mirror: 1h red, two greens covering and closing above → next session gap up.
/// </summary>
public static class HourEngulfGapStudy
{
    public const string ClueGreenCoverGapDown = "HOUR_GREEN_TWO_RED_COVER_GAP_DOWN";
    public const string ClueRedCoverGapUp = "HOUR_RED_TWO_GREEN_COVER_GAP_UP";
    public const string ClueGreenCoverGapDownPm = "HOUR_GREEN_TWO_RED_COVER_GAP_DOWN_PM";
    public const string ClueRedCoverGapUpPm = "HOUR_RED_TWO_GREEN_COVER_GAP_UP_PM";

    public const decimal MinGapPercent = GapDayStudy.MinGapPercent;

    public static HourEngulfSymbolResult Analyze(string symbol, IReadOnlyList<ChartBar> bars5m)
    {
        var ist = IndiaTz();
        var daily = BuildDailyOhlc(bars5m, ist);
        if (daily.Count < 4)
        {
            return HourEngulfSymbolResult.Empty(symbol);
        }

        var hourlyByDay = BuildHourlyBySessionDay(bars5m, ist);
        var clues = new Dictionary<string, EngulfClueBucket>
        {
            [ClueGreenCoverGapDown] = new(),
            [ClueRedCoverGapUp] = new(),
            [ClueGreenCoverGapDownPm] = new(),
            [ClueRedCoverGapUpPm] = new(),
        };

        for (var d = 0; d < daily.Count - 1; d++)
        {
            var today = daily[d];
            var next = daily[d + 1];
            if (!hourlyByDay.TryGetValue(today.Date, out var hours) || hours.Count < 3)
            {
                continue;
            }

            var gapPct = today.Close > 0 ? (next.Open - today.Close) / today.Close * 100m : 0m;

            for (var i = 0; i <= hours.Count - 3; i++)
            {
                var h0 = hours[i];
                var h1 = hours[i + 1];
                var h2 = hours[i + 2];
                var pm = IsSecondHalf(h2);

                if (TryGreenTwoRedCover(h0, h1, h2))
                {
                    RecordGap(clues[ClueGreenCoverGapDown], gapPct, bearishGap: true);
                    if (pm)
                    {
                        RecordGap(clues[ClueGreenCoverGapDownPm], gapPct, bearishGap: true);
                    }
                }

                if (TryRedTwoGreenCover(h0, h1, h2))
                {
                    RecordGap(clues[ClueRedCoverGapUp], gapPct, bearishGap: false);
                    if (pm)
                    {
                        RecordGap(clues[ClueRedCoverGapUpPm], gapPct, bearishGap: false);
                    }
                }
            }
        }

        return new HourEngulfSymbolResult(
            symbol,
            daily.Count,
            clues.ToDictionary(kv => kv.Key, kv => kv.Value.ToStats()));
    }

    public static HourEngulfUniverseReport Aggregate(string universe, IReadOnlyList<HourEngulfSymbolResult> perSymbol) =>
        HourEngulfReportBuilder.Build(universe, perSymbol);

    /// <summary>Green 1h, then two red 1h bars covering it; second red closes below green open.</summary>
    public static bool TryGreenTwoRedCover(HourBar g, HourBar r1, HourBar r2)
    {
        if (g.Close <= g.Open || r1.Close >= r1.Open || r2.Close >= r2.Open)
        {
            return false;
        }

        var sweepHigh = Math.Max(r1.High, r2.High);
        if (sweepHigh < g.High * (1m - ZoneBoardPattern.TouchTolerance))
        {
            return false;
        }

        return r2.Close < g.Open;
    }

    /// <summary>Red 1h, then two green 1h bars covering it; second green closes above red open.</summary>
    public static bool TryRedTwoGreenCover(HourBar r, HourBar g1, HourBar g2)
    {
        if (r.Close >= r.Open || g1.Close <= g1.Open || g2.Close <= g2.Open)
        {
            return false;
        }

        var sweepLow = Math.Min(g1.Low, g2.Low);
        if (sweepLow > r.Low * (1m + ZoneBoardPattern.TouchTolerance))
        {
            return false;
        }

        return g2.Close > r.Open;
    }

    private static void RecordGap(EngulfClueBucket bucket, decimal gapPct, bool bearishGap)
    {
        bucket.Events++;
        bucket.GapSum += gapPct;
        var gapHit = bearishGap ? gapPct <= -MinGapPercent : gapPct >= MinGapPercent;
        if (gapHit)
        {
            bucket.GapHitDays++;
        }

        if (bearishGap && gapPct < 0)
        {
            bucket.DirectionMatchDays++;
        }
        else if (!bearishGap && gapPct > 0)
        {
            bucket.DirectionMatchDays++;
        }
    }

    private static bool IsSecondHalf(HourBar bar) => bar.HourStartIst >= 12;

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

    public static Dictionary<DateOnly, List<HourBar>> BuildHourlyBySessionDayPublic(
        IReadOnlyList<ChartBar> bars,
        TimeZoneInfo ist) =>
        BuildHourlyBySessionDay(bars, ist);

    private static Dictionary<DateOnly, List<HourBar>> BuildHourlyBySessionDay(IReadOnlyList<ChartBar> bars, TimeZoneInfo ist)
    {
        var map = new Dictionary<DateOnly, List<HourBar>>();
        foreach (var g in bars.GroupBy(b => IstDate(ist, b.Time)))
        {
            var dayBars = g.OrderBy(b => b.Time).ToList();
            var hours = new List<HourBar>();
            foreach (var hg in dayBars.GroupBy(b =>
                     {
                         var t = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(b.Time), ist);
                         return t.Hour;
                     }))
            {
                var t0 = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(hg.Min(x => x.Time)), ist);
                var minutes = t0.Hour * 60 + t0.Minute;
                if (minutes < 9 * 60 + 15 || minutes >= 15 * 60 + 30)
                {
                    continue;
                }

                var list = hg.OrderBy(x => x.Time).ToList();
                hours.Add(new HourBar(
                    g.Key,
                    t0.Hour,
                    list[0].Open,
                    list.Max(x => x.High),
                    list.Min(x => x.Low),
                    list[^1].Close));
            }

            hours = hours.OrderBy(h => h.HourStartIst).ToList();
            if (hours.Count > 0)
            {
                map[g.Key] = hours;
            }
        }

        return map;
    }

    private static TimeZoneInfo IndiaTz() => TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    private static DateOnly IstDate(TimeZoneInfo ist, long unixSeconds) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), ist).DateTime);

    public sealed record HourBar(DateOnly SessionDate, int HourStartIst, decimal Open, decimal High, decimal Low, decimal Close);

    private sealed record DailyOhlc(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close);

    private sealed class EngulfClueBucket
    {
        public int Events;
        public int GapHitDays;
        public int DirectionMatchDays;
        public decimal GapSum;

        public HourEngulfClueStats ToStats()
        {
            var gapHitRate = Events > 0 ? (decimal)GapHitDays / Events * 100m : 0m;
            var dirRate = Events > 0 ? (decimal)DirectionMatchDays / Events * 100m : 0m;
            var avgGap = Events > 0 ? GapSum / Events : 0m;
            var fadeAvoidAdverse = Events - GapHitDays;
            var fadeRate = Events > 0 ? (decimal)fadeAvoidAdverse / Events * 100m : 0m;
            return new HourEngulfClueStats(
                Events,
                GapHitDays,
                gapHitRate,
                fadeAvoidAdverse,
                fadeRate,
                DirectionMatchDays,
                dirRate,
                avgGap,
                Events >= GapDayStudy.MinSamplesPerClue);
        }
    }
}

public sealed record HourEngulfClueStats(
    int PatternHits,
    int GapThresholdHits,
    decimal GapThresholdHitRatePercent,
    int FadeAvoidAdverseGapHits,
    decimal FadeAvoidAdverseGapRatePercent,
    int GapDirectionMatchDays,
    decimal GapDirectionMatchRatePercent,
    decimal AvgNextGapPercent,
    bool MeetsMinSamples);

public sealed record HourEngulfSymbolResult(
    string Symbol,
    int SessionDays,
    IReadOnlyDictionary<string, HourEngulfClueStats> Clues)
{
    public static HourEngulfSymbolResult Empty(string symbol) =>
        new(symbol, 0, new Dictionary<string, HourEngulfClueStats>());
}

public sealed record HourEngulfClueSummary(
    string ClueId,
    string Title,
    string PatternRule,
    string GapExpectation,
    string FadePlaybook,
    int TotalPatternHits,
    int SymbolsWithHits,
    int GapThresholdHits,
    decimal GapThresholdHitRatePercent,
    int FadeAvoidAdverseGapHits,
    decimal FadeAvoidAdverseGapRatePercent,
    decimal AvgNextGapPercent,
    decimal GapDirectionMatchRatePercent,
    bool MeetsMinSamples);

public sealed record HourEngulfUniverseReport(
    string Universe,
    int SymbolsProcessed,
    IReadOnlyList<HourEngulfClueSummary> Clues,
    string Verdict);

public static class HourEngulfReportBuilder
{
    private static readonly IReadOnlyList<(string Id, string Title, string Pattern, string GapExp)> Meta =
    [
        (
            HourEngulfGapStudy.ClueGreenCoverGapDown,
            "1h green → two red cover → gap down",
            "Hour 1 bullish; hours 2–3 bearish with highs covering green high; 2nd red close below green open.",
            "Next session open gaps down ≥ 0.35% vs prior close."),
        (
            HourEngulfGapStudy.ClueRedCoverGapUp,
            "1h red → two green cover → gap up",
            "Hour 1 bearish; hours 2–3 bullish covering red low; 2nd green close above red open.",
            "Next session open gaps up ≥ 0.35% vs prior close."),
        (
            HourEngulfGapStudy.ClueGreenCoverGapDownPm,
            "PM: 1h green → two red cover → gap down",
            "Same as green/red cover, pattern completes after 12:00 IST.",
            "Next session gap down ≥ 0.35%."),
        (
            HourEngulfGapStudy.ClueRedCoverGapUpPm,
            "PM: 1h red → two green cover → gap up",
            "Same as red/green cover, pattern completes after 12:00 IST.",
            "Next session gap up ≥ 0.35%."),
    ];

    public static HourEngulfUniverseReport Build(string universe, IReadOnlyList<HourEngulfSymbolResult> perSymbol)
    {
        var summaries = new List<HourEngulfClueSummary>();
        foreach (var (id, title, pattern, gapExp) in Meta)
        {
            var hits = 0;
            var gapHits = 0;
            var symbols = 0;
            var gapSum = 0m;
            var dirMatchSum = 0m;
            var dirN = 0;
            foreach (var sym in perSymbol)
            {
                if (!sym.Clues.TryGetValue(id, out var c) || c.PatternHits == 0)
                {
                    continue;
                }

                symbols++;
                hits += c.PatternHits;
                gapHits += c.GapThresholdHits;
                gapSum += c.AvgNextGapPercent * c.PatternHits;
                if (c.PatternHits > 0)
                {
                    dirMatchSum += c.GapDirectionMatchRatePercent;
                    dirN++;
                }
            }

            var gapHitRate = hits > 0 ? (decimal)gapHits / hits * 100m : 0m;
            var fadeHits = hits - gapHits;
            var fadeRate = hits > 0 ? (decimal)fadeHits / hits * 100m : 0m;
            var avgGap = hits > 0 ? gapSum / hits : 0m;
            var dirMatch = dirN > 0 ? dirMatchSum / dirN : 0m;
            var fadePlaybook = id.Contains("GAP_DOWN", StringComparison.Ordinal)
                ? "Fade: bet overnight long (no gap down ≥ 0.35%)"
                : "Fade: bet overnight short / avoid gap up ≥ 0.35%";
            summaries.Add(new HourEngulfClueSummary(
                id,
                title,
                pattern,
                gapExp,
                fadePlaybook,
                hits,
                symbols,
                gapHits,
                gapHitRate,
                fadeHits,
                fadeRate,
                avgGap,
                dirMatch,
                hits >= GapDayStudy.MinSamplesPerClue));
        }

        var bestDown = summaries
            .Where(s => s.ClueId.Contains("GAP_DOWN", StringComparison.Ordinal))
            .OrderByDescending(s => s.GapThresholdHitRatePercent)
            .FirstOrDefault();
        var bestUp = summaries
            .Where(s => s.ClueId.Contains("GAP_UP", StringComparison.Ordinal))
            .OrderByDescending(s => s.GapThresholdHitRatePercent)
            .FirstOrDefault();
        var verdict = bestDown is null || bestUp is null
            ? "Insufficient 1h engulf samples — widen lookback or refine rules."
            : $"Original gap bet weak ({bestDown.GapThresholdHitRatePercent:F1}% gap↓). "
              + $"Fade (no adverse gap): green→red {bestDown.FadeAvoidAdverseGapRatePercent:F1}% ({bestDown.FadeAvoidAdverseGapHits}/{bestDown.TotalPatternHits}), "
              + $"red→green {bestUp.FadeAvoidAdverseGapRatePercent:F1}%. "
              + $"Avg next gap% still ~{bestDown.AvgNextGapPercent:F2} after green→red — check expectancy before auto.";

        return new HourEngulfUniverseReport(universe, perSymbol.Count, summaries, verdict);
    }
}
