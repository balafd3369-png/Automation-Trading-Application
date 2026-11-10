using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed record GapFailedRallySignal(
    string Symbol,
    string Strategy,
    decimal GapPercent,
    decimal PriorOpen,
    decimal PriorClose,
    decimal PriorB1,
    decimal TodayOpen,
    long SignalBarUnix,
    decimal Entry,
    decimal StopLoss,
    decimal Target,
    decimal RiskPoints,
    decimal RewardPoints,
    IReadOnlyList<string> Evidence);

/// <summary>
/// Live gap playbooks (Nifty 50 stocks) — paper auto when gap signal + backtest gate pass.
/// </summary>
public static class GapFailedRallyPattern
{
    public const string ChartInterval = ZoneBoardPattern.ChartInterval;

    private static readonly TimeOnly EntryWindowStart = new(9, 20);
    private static readonly TimeOnly EntryWindowEnd = new(10, 45);

    private static readonly string[] CluePriority =
    [
        GapDayStudy.ClueFailedRallyGapDown,
        GapDayStudy.ClueDistributionGapDown,
        GapDayStudy.ClueWeakCloseGapDown,
        GapDayStudy.ClueStrongCloseGapUp,
    ];

    public static bool TryBuildLiveSignal(
        SymbolSnapshot symbol,
        IReadOnlyList<ChartBar> bars,
        DateTimeOffset nowIst,
        out GapFailedRallySignal? signal)
    {
        signal = null;
        if (!HasSaneSessionOpen(symbol))
        {
            return false;
        }

        foreach (var clue in CluePriority)
        {
            if (TryBuildLiveSignalForClue(symbol, bars, nowIst, clue, out signal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryBuildLiveSignalForClue(
        SymbolSnapshot symbol,
        IReadOnlyList<ChartBar> bars,
        DateTimeOffset nowIst,
        string clueId,
        out GapFailedRallySignal? signal)
    {
        signal = null;
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var tIst = TimeZoneInfo.ConvertTime(nowIst, ist).TimeOfDay;
        if (tIst < EntryWindowStart.ToTimeSpan() || tIst > EntryWindowEnd.ToTimeSpan())
        {
            return false;
        }

        if (bars.Count < 30)
        {
            return false;
        }

        var zones = ZoneBoardPattern.BuildDailyZones(bars, ist);
        var daily = bars
            .GroupBy(b => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(b.Time), ist).DateTime))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var day = g.OrderBy(x => x.Time).ToList();
                return (Date: g.Key, Open: day[0].Open, High: day.Max(x => x.High), Low: day.Min(x => x.Low), Close: day[^1].Close);
            })
            .ToList();

        if (daily.Count < 3)
        {
            return false;
        }

        var today = daily[^1];
        var prior = daily[^2];
        if (!zones.TryGetValue(today.Date, out var zToday) || !zones.TryGetValue(prior.Date, out var zPrior))
        {
            return false;
        }

        if (prior.Close <= 0)
        {
            return false;
        }

        var tol = ZoneBoardPattern.TouchTolerance;
        var gapPct = (symbol.TodayOpen - prior.Close) / prior.Close * 100m;
        var bullish = clueId == GapDayStudy.ClueStrongCloseGapUp;
        if (bullish)
        {
            if (gapPct < GapDayStudy.MinGapPercent)
            {
                return false;
            }

            if (prior.Close < zPrior.B1 * (1m - tol))
            {
                return false;
            }
        }
        else
        {
            if (gapPct > -GapDayStudy.MinGapPercent)
            {
                return false;
            }

            var matches = clueId switch
            {
                GapDayStudy.ClueFailedRallyGapDown => prior.Open >= zPrior.B1 * (1m - tol)
                    && prior.Close < prior.Open
                    && prior.Close < zPrior.B1,
                GapDayStudy.ClueDistributionGapDown => prior.Open >= zPrior.B1 * (1m - tol)
                    && prior.Close <= zPrior.S1 * (1m + tol),
                GapDayStudy.ClueWeakCloseGapDown => prior.Close < zPrior.S1 * (1m - tol),
                _ => false,
            };

            if (!matches)
            {
                return false;
            }
        }

        var entry = symbol.Last > 0 ? symbol.Last : symbol.TodayOpen;
        var todayBars = bars
            .Where(b => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(b.Time), ist).DateTime) == today.Date)
            .ToList();
        var sessionHigh = todayBars.Select(b => b.High).DefaultIfEmpty(entry).Max();
        var sessionLow = todayBars.Select(b => b.Low).DefaultIfEmpty(entry).Min();

        decimal stop;
        decimal target;
        if (bullish)
        {
            stop = Math.Min(sessionLow, entry) * (1m - tol);
            target = zToday.B2;
            if (target <= entry || stop >= entry)
            {
                return false;
            }
        }
        else
        {
            stop = Math.Max(sessionHigh, entry) * (1m + tol);
            target = zToday.S2;
            if (riskInvalid(entry, stop, target, bearish: true))
            {
                return false;
            }
        }

        if (bullish && riskInvalid(entry, stop, target, bearish: false))
        {
            return false;
        }

        var risk = bullish ? entry - stop : stop - entry;
        var reward = bullish ? target - entry : entry - target;

        var signalBar = todayBars.OrderBy(b => b.Time).FirstOrDefault();
        var signalUnix = signalBar?.Time ?? bars[^1].Time;
        var candleNote = signalBar is null
            ? "signal bar n/a"
            : $"signal 5m O={signalBar.Open:0.##} H={signalBar.High:0.##} L={signalBar.Low:0.##} C={signalBar.Close:0.##}";

        var priorNote = clueId switch
        {
            GapDayStudy.ClueFailedRallyGapDown =>
                $"Prior failed rally: O {prior.Open:0.##} ≥ B1 {zPrior.B1:0.##}, C {prior.Close:0.##} < B1",
            GapDayStudy.ClueDistributionGapDown =>
                $"Prior distribution: O {prior.Open:0.##} ≥ B1 {zPrior.B1:0.##}, C {prior.Close:0.##} ≤ S1 {zPrior.S1:0.##}",
            GapDayStudy.ClueWeakCloseGapDown =>
                $"Prior weak close: C {prior.Close:0.##} < S1 {zPrior.S1:0.##}",
            GapDayStudy.ClueStrongCloseGapUp =>
                $"Prior strong close: C {prior.Close:0.##} ≥ B1 {zPrior.B1:0.##}",
            _ => "Prior session context",
        };

        var gapNote = bullish
            ? $"Gap up {gapPct:0.##}% vs prior close {prior.Close:0.##}"
            : $"Gap down {gapPct:0.##}% vs prior close {prior.Close:0.##}";
        var exitNote = bullish
            ? $"Targets today B2 {target:0.##}, stop {stop:0.##}"
            : $"Targets today S2 {target:0.##}, stop {stop:0.##}";

        signal = new GapFailedRallySignal(
            symbol.Name,
            clueId,
            gapPct,
            prior.Open,
            prior.Close,
            zPrior.B1,
            symbol.TodayOpen,
            signalUnix,
            entry,
            stop,
            target,
            risk,
            reward,
            [priorNote, gapNote, candleNote, exitNote]);
        return true;
    }

    private static bool riskInvalid(decimal entry, decimal stop, decimal target, bool bearish)
    {
        if (bearish)
        {
            return stop <= entry || rewardInvalid(entry, target, bearish: true);
        }

        return stop >= entry || rewardInvalid(entry, target, bearish: false);
    }

    private static bool rewardInvalid(decimal entry, decimal target, bool bearish) =>
        bearish ? target >= entry : target <= entry;

    private static bool HasSaneSessionOpen(SymbolSnapshot snap)
    {
        if (snap.TodayOpen <= 0 || snap.Last <= 0 || snap.PreviousClose <= 0)
        {
            return false;
        }

        const decimal maxOpenVsLastDriftPct = 6m;
        var openVsLast = Math.Abs(snap.TodayOpen - snap.Last) / snap.Last * 100m;
        return openVsLast <= maxOpenVsLastDriftPct;
    }
}
