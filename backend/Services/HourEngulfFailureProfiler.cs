using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Finds what repeats on fade failures (adverse gap) for 1h engulf patterns so we can skip those days.
/// </summary>
public static class HourEngulfFailureProfiler
{
    public sealed record HourEngulfEvent(
        string Symbol,
        string ClueId,
        DateOnly PatternDate,
        decimal NextGapPercent,
        bool FadeFailure,
        bool PatternCompletesPm,
        int PatternEndHourIst,
        bool PriorCloseBelowS1,
        bool PriorCloseAboveB1,
        bool PriorFailedRally,
        bool PatternDayCloseBelowOpen,
        bool DeepRedCover,
        decimal PatternDayReturnPercent,
        string DayOfWeek);

    public static IReadOnlyList<HourEngulfEvent> CollectEvents(string symbol, IReadOnlyList<ChartBar> bars5m, string clueId)
    {
        var ist = IndiaTz();
        var daily = BuildDailyOhlc(bars5m, ist);
        var zones = ZoneBoardPattern.BuildDailyZones(bars5m, ist);
        var hourlyByDay = HourEngulfGapStudy.BuildHourlyBySessionDayPublic(bars5m, ist);
        var events = new List<HourEngulfEvent>();
        if (daily.Count < 4)
        {
            return events;
        }

        var bearishFade = clueId.Contains("GAP_DOWN", StringComparison.Ordinal);

        for (var d = 1; d < daily.Count - 1; d++)
        {
            var today = daily[d];
            var next = daily[d + 1];
            var prior = daily[d - 1];
            if (!hourlyByDay.TryGetValue(today.Date, out var hours) || hours.Count < 3)
            {
                continue;
            }

            zones.TryGetValue(prior.Date, out var zPrior);
            var tol = ZoneBoardPattern.TouchTolerance;
            var priorBelowS1 = zPrior is not null && prior.Close < zPrior.S1 * (1m - tol);
            var priorAboveB1 = zPrior is not null && prior.Close >= zPrior.B1 * (1m - tol);
            var priorFailedRally = zPrior is not null
                && prior.Open >= zPrior.B1 * (1m - tol)
                && prior.Close < prior.Open
                && prior.Close < zPrior.B1;
            var patternDayRet = today.Open > 0 ? (today.Close - today.Open) / today.Open * 100m : 0m;
            var patternDayRed = today.Close < today.Open;

            var gapPct = today.Close > 0 ? (next.Open - today.Close) / today.Close * 100m : 0m;

            for (var i = 0; i <= hours.Count - 3; i++)
            {
                var h0 = hours[i];
                var h1 = hours[i + 1];
                var h2 = hours[i + 2];
                var match = bearishFade
                    ? HourEngulfGapStudy.TryGreenTwoRedCover(h0, h1, h2)
                    : HourEngulfGapStudy.TryRedTwoGreenCover(h0, h1, h2);
                if (!match)
                {
                    continue;
                }

                var pmOnly = clueId.EndsWith("_PM", StringComparison.Ordinal);
                var pm = h2.HourStartIst >= 12;
                if (pmOnly && !pm)
                {
                    continue;
                }

                var adverse = bearishFade
                    ? gapPct <= -HourEngulfGapStudy.MinGapPercent
                    : gapPct >= HourEngulfGapStudy.MinGapPercent;
                var deepCover = bearishFade
                    ? h2.Close < h0.Low
                    : h2.Close > h0.High;

                events.Add(new HourEngulfEvent(
                    symbol,
                    clueId,
                    today.Date,
                    gapPct,
                    adverse,
                    pm,
                    h2.HourStartIst,
                    priorBelowS1,
                    priorAboveB1,
                    priorFailedRally,
                    patternDayRed,
                    deepCover,
                    patternDayRet,
                    today.Date.DayOfWeek.ToString()));
            }
        }

        return events;
    }

    public static HourEngulfFailureProfile BuildProfile(
        IReadOnlyList<HourEngulfEvent> events,
        string strategyLabel)
    {
        if (events.Count == 0)
        {
            return new HourEngulfFailureProfile(strategyLabel, 0, 0, 0, 0, [], []);
        }

        var failures = events.Count(e => e.FadeFailure);
        var failRate = (decimal)failures / events.Count * 100m;
        var winRate = 100m - failRate;

        var factors = new List<HourEngulfFailureFactor>();
        void AddFactor(string name, Func<HourEngulfEvent, bool> whenTrue)
        {
            var yes = events.Where(whenTrue).ToList();
            var no = events.Where(e => !whenTrue(e)).ToList();
            if (yes.Count < 5 || no.Count < 5)
            {
                return;
            }

            var failYes = yes.Count(e => e.FadeFailure);
            var failNo = no.Count(e => e.FadeFailure);
            var rateYes = (decimal)failYes / yes.Count * 100m;
            var rateNo = (decimal)failNo / no.Count * 100m;
            factors.Add(new HourEngulfFailureFactor(
                name,
                yes.Count,
                rateYes,
                no.Count,
                rateNo,
                rateYes - rateNo));
        }

        AddFactor("Prior close below S1", e => e.PriorCloseBelowS1);
        AddFactor("Prior failed rally (open≥B1, close<B1)", e => e.PriorFailedRally);
        AddFactor("Prior close above B1", e => e.PriorCloseAboveB1);
        AddFactor("Pattern day red close", e => e.PatternDayCloseBelowOpen);
        AddFactor("Pattern completes PM (≥12:00)", e => e.PatternCompletesPm);
        AddFactor("Pattern ends 14:00+ IST", e => e.PatternEndHourIst >= 14);
        AddFactor("Deep cover (2nd bar pierces anchor low/high)", e => e.DeepRedCover);
        AddFactor("Pattern day loss > 1%", e => e.PatternDayReturnPercent < -1m);
        AddFactor("Pattern day gain > 1%", e => e.PatternDayReturnPercent > 1m);
        AddFactor("Friday pattern day", e => e.DayOfWeek == nameof(DayOfWeek.Friday));
        AddFactor("Monday pattern day", e => e.DayOfWeek == nameof(DayOfWeek.Monday));

        var ranked = factors.OrderByDescending(f => f.FailRateLiftWhenTrue).ToList();
        var filters = new List<HourEngulfFilterSuggestion>();

        foreach (var f in ranked.Where(f => f.FailRateLiftWhenTrue >= 8m))
        {
            var kept = events.Where(e => !FactorPredicate(f.Name, e)).ToList();
            if (kept.Count < 8)
            {
                continue;
            }

            var newFail = kept.Count(e => e.FadeFailure);
            var newWin = 100m - (decimal)newFail / kept.Count * 100m;
            filters.Add(new HourEngulfFilterSuggestion(
                $"Skip when: {f.Name}",
                kept.Count,
                newWin,
                100m - (decimal)events.Count(e => e.FadeFailure) / events.Count * 100m,
                events.Count - kept.Count));
        }

        filters = filters.OrderByDescending(f => f.WinRateAfterFilterPercent).Take(5).ToList();

        return new HourEngulfFailureProfile(
            strategyLabel,
            events.Count,
            failures,
            failRate,
            winRate,
            ranked.Take(12).ToList(),
            filters);
    }

    private static bool FactorPredicate(string name, HourEngulfEvent e) => name switch
    {
        "Prior close below S1" => e.PriorCloseBelowS1,
        "Prior failed rally (open≥B1, close<B1)" => e.PriorFailedRally,
        "Prior close above B1" => e.PriorCloseAboveB1,
        "Pattern day red close" => e.PatternDayCloseBelowOpen,
        "Pattern completes PM (≥12:00)" => e.PatternCompletesPm,
        "Pattern ends 14:00+ IST" => e.PatternEndHourIst >= 14,
        "Deep cover (2nd bar pierces anchor low/high)" => e.DeepRedCover,
        "Pattern day loss > 1%" => e.PatternDayReturnPercent < -1m,
        "Pattern day gain > 1%" => e.PatternDayReturnPercent > 1m,
        "Friday pattern day" => e.DayOfWeek == nameof(DayOfWeek.Friday),
        "Monday pattern day" => e.DayOfWeek == nameof(DayOfWeek.Monday),
        _ => false,
    };

    private static List<DailyOhlc> BuildDailyOhlc(IReadOnlyList<ChartBar> bars, TimeZoneInfo ist) =>
        bars
            .GroupBy(bar => IstDate(ist, bar.Time))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var day = g.OrderBy(b => b.Time).ToList();
                return new DailyOhlc(g.Key, day[0].Open, day.Max(b => b.High), day.Min(b => b.Low), day[^1].Close);
            })
            .ToList();

    private static TimeZoneInfo IndiaTz() => TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    private static DateOnly IstDate(TimeZoneInfo ist, long unixSeconds) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), ist).DateTime);

    private sealed record DailyOhlc(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close);
}

/// <summary>Exposes hourly session builder for profiler.</summary>
public sealed record HourEngulfFailureFactor(
    string Name,
    int CountWhenTrue,
    decimal FailRateWhenTruePercent,
    int CountWhenFalse,
    decimal FailRateWhenFalsePercent,
    decimal FailRateLiftWhenTrue);

public sealed record HourEngulfFilterSuggestion(
    string Rule,
    int TradesAfterFilter,
    decimal WinRateAfterFilterPercent,
    decimal BaselineWinRatePercent,
    int SkippedTrades);

public sealed record HourEngulfFailureProfile(
    string Strategy,
    int TotalEvents,
    int FadeFailures,
    decimal FadeFailureRatePercent,
    decimal FadeWinRatePercent,
    IReadOnlyList<HourEngulfFailureFactor> Factors,
    IReadOnlyList<HourEngulfFilterSuggestion> SuggestedFilters);
