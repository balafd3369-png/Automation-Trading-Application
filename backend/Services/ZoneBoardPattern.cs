using TradeWithBeast.Formulas;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Trade-with-Beast zone board rules (TradingView reference charts): B2/B1 buy zones, S1/S2 sell zones.
/// All levels come from <see cref="NiftyZoneFormula"/> on each session's OHLC chain.
/// </summary>
public static class ZoneBoardPattern
{
    public const string B1BreakLong = "ZONE_B1_BREAK_B2";
    public const string S1BreakShort = "ZONE_S1_BREAK_S2";
    public const string S2BounceLong = "ZONE_S2_BOUNCE_B1";

    public const string ChartInterval = "5minute";
    public const decimal TouchTolerance = 0.0015m;

    public const int MinBacktestSamples = 8;
    public const decimal MinWinRateToRecommend = 55m;

    public sealed record ZoneBoardLevels(
        decimal B2,
        decimal B1,
        decimal S,
        decimal S1,
        decimal S2);

    public sealed record ZoneBoardBacktestResult(
        string Symbol,
        string Pattern,
        int Samples,
        int Wins,
        int Losses,
        decimal WinRatePercent,
        decimal AvgRewardPoints,
        decimal AvgRiskPoints);

    public static ZoneBoardLevels Levels(
        decimal yesterdayHigh,
        decimal yesterdayLow,
        decimal highTwoDaysAgo,
        decimal lowTwoDaysAgo,
        decimal todayOpen) =>
        new(
            NiftyZoneFormula.RoundLine(NiftyZoneFormula.B2(highTwoDaysAgo, yesterdayHigh)),
            NiftyZoneFormula.RoundLine(NiftyZoneFormula.B1(yesterdayHigh, todayOpen)),
            NiftyZoneFormula.RoundLine(NiftyZoneFormula.S(yesterdayHigh, yesterdayLow, todayOpen)),
            NiftyZoneFormula.RoundLine(NiftyZoneFormula.S1(yesterdayLow, todayOpen)),
            NiftyZoneFormula.RoundLine(NiftyZoneFormula.S2(lowTwoDaysAgo, yesterdayLow)));

    public static bool Qualifies(ZoneBoardBacktestResult result) =>
        result.Samples >= MinBacktestSamples && result.WinRatePercent >= MinWinRateToRecommend;

    public static ZoneBoardBacktestResult BacktestB1BreakLong(string symbol, IReadOnlyList<ChartBar> bars) =>
        BacktestCross(
            symbol,
            B1BreakLong,
            bars,
            bullish: true,
            crossLevel: (z, _) => z.B1,
            stop: (z, entry, bar) => z.S,
            target: (z, _, _) => z.B2,
            validate: (z, entry, stop, target) =>
                z.B1 < z.B2 && entry > z.B1 && stop < entry && target > entry
                && !NiftyZoneFormula.IsTightRange(symbol, z.B1, z.S1, z.S));

    public static ZoneBoardBacktestResult BacktestS1BreakShort(string symbol, IReadOnlyList<ChartBar> bars) =>
        BacktestCross(
            symbol,
            S1BreakShort,
            bars,
            bullish: false,
            crossLevel: (z, _) => z.S1,
            stop: (z, entry, bar) => z.S,
            target: (z, _, _) => z.S2,
            validate: (z, entry, stop, target) =>
                z.S2 < z.S1 && entry < z.S1 && stop > entry && target < entry
                && !NiftyZoneFormula.IsTightRange(symbol, z.B1, z.S1, z.S));

    public static ZoneBoardBacktestResult BacktestS2BounceLong(string symbol, IReadOnlyList<ChartBar> bars) =>
        BacktestBounce(
            symbol,
            S2BounceLong,
            bars,
            support: (z, _) => z.S2,
            stop: (z, entry, bar) => Math.Min(z.S2, bar.Low) * (1m - TouchTolerance),
            target: (z, _, _) => z.B1,
            bullish: true,
            validate: (z, entry, stop, target) =>
                z.S2 < z.B1 && entry > z.S2 && stop < entry && target > entry
                && !NiftyZoneFormula.IsTightRange(symbol, z.B1, z.S1, z.S));

    public sealed record ZoneBoardTradeJournalEntry(
        string Symbol,
        string Pattern,
        string Direction,
        DateOnly SessionDate,
        DateTimeOffset EntryTime,
        decimal Entry,
        decimal StopLoss,
        decimal Target,
        decimal B2,
        decimal B1,
        decimal S,
        decimal S1,
        decimal S2,
        DateTimeOffset ExitTime,
        decimal ExitPrice,
        string ExitReason,
        decimal PointsPnl,
        string Result,
        string EntryRule);

    public static IReadOnlyList<ZoneBoardTradeJournalEntry> JournalB1BreakLong(string symbol, IReadOnlyList<ChartBar> bars) =>
        JournalCross(
            symbol,
            B1BreakLong,
            "LONG",
            bars,
            bullish: true,
            crossLevel: (z, _) => z.B1,
            stop: (z, entry, bar) => z.S,
            target: (z, _, _) => z.B2,
            validate: (z, entry, stop, target) =>
                z.B1 < z.B2 && entry > z.B1 && stop < entry && target > entry
                && !NiftyZoneFormula.IsTightRange(symbol, z.B1, z.S1, z.S),
            entryRule:
                "5m: prior candle close <= B1, current candle closes above B1 -> enter at close (long).");

    public static IReadOnlyList<ZoneBoardTradeJournalEntry> JournalS1BreakShort(string symbol, IReadOnlyList<ChartBar> bars) =>
        JournalCross(
            symbol,
            S1BreakShort,
            "SHORT",
            bars,
            bullish: false,
            crossLevel: (z, _) => z.S1,
            stop: (z, entry, bar) => z.S,
            target: (z, _, _) => z.S2,
            validate: (z, entry, stop, target) =>
                z.S2 < z.S1 && entry < z.S1 && stop > entry && target < entry
                && !NiftyZoneFormula.IsTightRange(symbol, z.B1, z.S1, z.S),
            entryRule:
                "5m: prior candle close >= S1, current candle closes below S1 -> enter at close (short).");

    public static ZoneBoardBacktestResult BacktestB1RejectionShort(string symbol, IReadOnlyList<ChartBar> bars)
    {
        var ist = IndiaTz();
        var zones = BuildDailyZones(bars, ist);
        var wins = 0;
        var losses = 0;
        decimal rewardSum = 0;
        decimal riskSum = 0;
        foreach (var session in bars.GroupBy(bar => IstDate(ist, bar.Time)))
        {
            if (!zones.TryGetValue(session.Key, out var z))
            {
                continue;
            }

            var sessionBars = session.OrderBy(b => b.Time).ToList();
            if (sessionBars.Count < 6 || z.B1 <= z.S2)
            {
                continue;
            }

            for (var i = 2; i < sessionBars.Count - 1; i++)
            {
                var priorHigh = sessionBars.Take(i).Max(b => b.High);
                var bar = sessionBars[i];
                if (priorHigh < z.B1 * (1m - TouchTolerance))
                {
                    continue;
                }

                if (bar.High < z.B1 * (1m - TouchTolerance) || bar.Close >= z.B1)
                {
                    continue;
                }

                var entry = bar.Close;
                var stop = Math.Max(bar.High, z.B1) * (1m + TouchTolerance);
                var risk = stop - entry;
                var reward = entry - z.S2;
                if (risk <= 0 || reward <= 0)
                {
                    continue;
                }

                var outcome = Simulate(sessionBars, i + 1, entry, stop, z.S2, bearish: true);
                if (outcome is null)
                {
                    continue;
                }

                if (outcome == true)
                {
                    wins++;
                    rewardSum += entry - z.S2;
                }
                else
                {
                    losses++;
                    riskSum += stop - entry;
                }
            }
        }

        return Finish(symbol, ZoneRejectionPattern.B1RejectionToS2, wins, losses, rewardSum, riskSum);
    }

    public static IReadOnlyDictionary<DateOnly, ZoneBoardLevels> BuildDailyZones(
        IReadOnlyList<ChartBar> bars,
        TimeZoneInfo? ist = null)
    {
        ist ??= IndiaTz();
        var ordered = bars
            .GroupBy(bar => IstDate(ist, bar.Time))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var dayBars = g.OrderBy(b => b.Time).ToList();
                return new DailyBar(
                    g.Key,
                    dayBars[0].Open,
                    dayBars.Max(b => b.High),
                    dayBars.Min(b => b.Low),
                    dayBars[^1].Close);
            })
            .ToList();

        var map = new Dictionary<DateOnly, ZoneBoardLevels>();
        for (var i = 2; i < ordered.Count; i++)
        {
            var today = ordered[i];
            var yesterday = ordered[i - 1];
            var twoAgo = ordered[i - 2];
            map[today.Date] = Levels(
                yesterday.High,
                yesterday.Low,
                twoAgo.High,
                twoAgo.Low,
                today.Open);
        }

        return map;
    }

    private static ZoneBoardBacktestResult BacktestCross(
        string symbol,
        string pattern,
        IReadOnlyList<ChartBar> bars,
        bool bullish,
        Func<ZoneBoardLevels, ChartBar, decimal> crossLevel,
        Func<ZoneBoardLevels, decimal, ChartBar, decimal> stop,
        Func<ZoneBoardLevels, decimal, ChartBar, decimal> target,
        Func<ZoneBoardLevels, decimal, decimal, decimal, bool> validate)
    {
        var ist = IndiaTz();
        var zones = BuildDailyZones(bars, ist);
        var wins = 0;
        var losses = 0;
        decimal rewardSum = 0;
        decimal riskSum = 0;

        foreach (var session in bars.GroupBy(bar => IstDate(ist, bar.Time)))
        {
            if (!zones.TryGetValue(session.Key, out var z))
            {
                continue;
            }

            var sessionBars = session.OrderBy(b => b.Time).ToList();
            if (sessionBars.Count < 4)
            {
                continue;
            }

            var level = crossLevel(z, sessionBars[0]);
            for (var i = 1; i < sessionBars.Count - 1; i++)
            {
                var prev = sessionBars[i - 1];
                var bar = sessionBars[i];
                var crossed = bullish
                    ? prev.Close <= level && bar.Close > level
                    : prev.Close >= level && bar.Close < level;
                if (!crossed)
                {
                    continue;
                }

                var entry = bar.Close;
                var stopPx = stop(z, entry, bar);
                var targetPx = target(z, entry, bar);
                if (!validate(z, entry, stopPx, targetPx))
                {
                    continue;
                }

                var risk = Math.Abs(entry - stopPx);
                var reward = Math.Abs(targetPx - entry);
                if (risk <= 0 || reward <= 0)
                {
                    continue;
                }

                var outcome = Simulate(sessionBars, i + 1, entry, stopPx, targetPx, bearish: !bullish);
                if (outcome is null)
                {
                    continue;
                }

                if (outcome == true)
                {
                    wins++;
                    rewardSum += reward;
                }
                else
                {
                    losses++;
                    riskSum += risk;
                }
            }
        }

        return Finish(symbol, pattern, wins, losses, rewardSum, riskSum);
    }

    private static ZoneBoardBacktestResult BacktestBounce(
        string symbol,
        string pattern,
        IReadOnlyList<ChartBar> bars,
        Func<ZoneBoardLevels, ChartBar, decimal> support,
        Func<ZoneBoardLevels, decimal, ChartBar, decimal> stop,
        Func<ZoneBoardLevels, decimal, ChartBar, decimal> target,
        bool bullish,
        Func<ZoneBoardLevels, decimal, decimal, decimal, bool> validate)
    {
        var ist = IndiaTz();
        var zones = BuildDailyZones(bars, ist);
        var wins = 0;
        var losses = 0;
        decimal rewardSum = 0;
        decimal riskSum = 0;

        foreach (var session in bars.GroupBy(bar => IstDate(ist, bar.Time)))
        {
            if (!zones.TryGetValue(session.Key, out var z))
            {
                continue;
            }

            var sessionBars = session.OrderBy(b => b.Time).ToList();
            if (sessionBars.Count < 4)
            {
                continue;
            }

            var supportPx = support(z, sessionBars[0]);
            var touched = false;
            for (var i = 1; i < sessionBars.Count - 1; i++)
            {
                var prev = sessionBars[i - 1];
                var bar = sessionBars[i];
                touched = touched || sessionBars.Take(i).Any(b => b.Low <= supportPx * (1m + TouchTolerance));
                var reclaimed = touched
                    && prev.Close <= supportPx
                    && bar.Close > supportPx * (1m + TouchTolerance);
                if (!reclaimed)
                {
                    continue;
                }

                var entry = bar.Close;
                var stopPx = stop(z, entry, bar);
                var targetPx = target(z, entry, bar);
                if (!validate(z, entry, stopPx, targetPx))
                {
                    continue;
                }

                var risk = entry - stopPx;
                var reward = targetPx - entry;
                if (risk <= 0 || reward <= 0)
                {
                    continue;
                }

                var outcome = Simulate(sessionBars, i + 1, entry, stopPx, targetPx, bearish: false);
                if (outcome is null)
                {
                    continue;
                }

                if (outcome == true)
                {
                    wins++;
                    rewardSum += reward;
                }
                else
                {
                    losses++;
                    riskSum += risk;
                }
            }
        }

        return Finish(symbol, pattern, wins, losses, rewardSum, riskSum);
    }

    private static ZoneBoardBacktestResult Finish(
        string symbol,
        string pattern,
        int wins,
        int losses,
        decimal rewardSum,
        decimal riskSum)
    {
        var samples = wins + losses;
        var winRate = samples == 0 ? 0 : decimal.Round((decimal)wins / samples * 100m, 2);
        return new(
            symbol,
            pattern,
            samples,
            wins,
            losses,
            winRate,
            samples == 0 ? 0 : decimal.Round(rewardSum / Math.Max(1, wins), 2),
            samples == 0 ? 0 : decimal.Round(riskSum / Math.Max(1, losses), 2));
    }

    private sealed record TradeSimulation(
        bool? Won,
        int ExitIndex,
        decimal ExitPrice,
        string ExitReason);

    private static IReadOnlyList<ZoneBoardTradeJournalEntry> JournalCross(
        string symbol,
        string pattern,
        string direction,
        IReadOnlyList<ChartBar> bars,
        bool bullish,
        Func<ZoneBoardLevels, ChartBar, decimal> crossLevel,
        Func<ZoneBoardLevels, decimal, ChartBar, decimal> stop,
        Func<ZoneBoardLevels, decimal, ChartBar, decimal> target,
        Func<ZoneBoardLevels, decimal, decimal, decimal, bool> validate,
        string entryRule)
    {
        var ist = IndiaTz();
        var zones = BuildDailyZones(bars, ist);
        var journal = new List<ZoneBoardTradeJournalEntry>();

        foreach (var session in bars.GroupBy(bar => IstDate(ist, bar.Time)))
        {
            if (!zones.TryGetValue(session.Key, out var z))
            {
                continue;
            }

            var sessionBars = session.OrderBy(b => b.Time).ToList();
            if (sessionBars.Count < 4)
            {
                continue;
            }

            var level = crossLevel(z, sessionBars[0]);
            for (var i = 1; i < sessionBars.Count - 1; i++)
            {
                var prev = sessionBars[i - 1];
                var bar = sessionBars[i];
                var crossed = bullish
                    ? prev.Close <= level && bar.Close > level
                    : prev.Close >= level && bar.Close < level;
                if (!crossed)
                {
                    continue;
                }

                var entry = bar.Close;
                var stopPx = stop(z, entry, bar);
                var targetPx = target(z, entry, bar);
                if (!validate(z, entry, stopPx, targetPx))
                {
                    continue;
                }

                var sim = SimulateTrade(sessionBars, i + 1, entry, stopPx, targetPx, bearish: !bullish);
                var exitBar = sessionBars[sim.ExitIndex];
                var entryTime = ToIst(ist, bar.Time);
                var exitTime = ToIst(ist, exitBar.Time);
                var pnl = bullish ? sim.ExitPrice - entry : entry - sim.ExitPrice;
                var result = sim.ExitReason switch
                {
                    "TARGET" => "WIN",
                    "STOP" => "LOSS",
                    _ => pnl > 0 ? "WIN_EOD" : pnl < 0 ? "LOSS_EOD" : "FLAT_EOD",
                };

                journal.Add(new ZoneBoardTradeJournalEntry(
                    symbol,
                    pattern,
                    direction,
                    session.Key,
                    entryTime,
                    decimal.Round(entry, 2),
                    decimal.Round(stopPx, 2),
                    decimal.Round(targetPx, 2),
                    z.B2,
                    z.B1,
                    z.S,
                    z.S1,
                    z.S2,
                    exitTime,
                    decimal.Round(sim.ExitPrice, 2),
                    sim.ExitReason,
                    decimal.Round(pnl, 2),
                    result,
                    entryRule));
            }
        }

        return journal;
    }

    private static bool? Simulate(
        IReadOnlyList<ChartBar> bars,
        int startIndex,
        decimal entry,
        decimal stop,
        decimal target,
        bool bearish)
    {
        var sim = SimulateTrade(bars, startIndex, entry, stop, target, bearish);
        return sim.ExitReason switch
        {
            "TARGET" => true,
            "STOP" => false,
            _ => null,
        };
    }

    private static TradeSimulation SimulateTrade(
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
                    return new TradeSimulation(false, i, stop, "STOP");
                }

                if (bar.Low <= target)
                {
                    return new TradeSimulation(true, i, target, "TARGET");
                }
            }
            else
            {
                if (bar.Low <= stop)
                {
                    return new TradeSimulation(false, i, stop, "STOP");
                }

                if (bar.High >= target)
                {
                    return new TradeSimulation(true, i, target, "TARGET");
                }
            }
        }

        var last = bars[^1];
        return new TradeSimulation(null, bars.Count - 1, last.Close, "SESSION_END");
    }

    private static DateTimeOffset ToIst(TimeZoneInfo ist, long unixSeconds) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), ist);

    private static TimeZoneInfo IndiaTz() =>
        TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    private static DateOnly IstDate(TimeZoneInfo ist, long unixSeconds)
    {
        var utc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, ist).DateTime);
    }

    private sealed record DailyBar(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close);
}
