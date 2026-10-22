using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed record TenAmSignal(
    string Strategy,
    string Symbol,
    decimal ReferenceLevel,
    decimal Entry,
    decimal StopLoss,
    decimal Target,
    long SignalBarTime,
    IReadOnlyList<string> Evidence);

public static class TenAmIntradayPattern
{
    public const string ChartInterval = "15minute";
    public const string HighFadeStrategy = "TEN_AM_HIGH_FADE_DAY_LOW";
    public const string LowBreakStrategy = "TEN_AM_LOW_BREAK_DAY_OPEN";
    public const decimal StopBufferPoints = StrategyCatalog.BreakoutCandleStopBufferPoints;

    private static readonly TimeSpan SessionStart = new(9, 15, 0);
    private static readonly TimeSpan ReferenceEnd = new(10, 0, 0);
    private static readonly TimeSpan FadeEarliest = new(11, 0, 0);
    private static readonly TimeSpan LowBreakEarliest = new(10, 0, 0);

    public static bool TryBuildHighFade(
        string symbol,
        IReadOnlyList<ChartBar> bars,
        decimal dayOpen,
        DateTimeOffset nowUtc,
        out TenAmSignal? signal)
    {
        signal = null;
        if (bars.Count < 4 || dayOpen <= 0)
        {
            return false;
        }

        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var nowIst = TimeZoneInfo.ConvertTime(nowUtc, ist);
        if (nowIst.TimeOfDay < FadeEarliest)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(nowIst.DateTime);
        var todayBars = SessionBars(bars, ist, today);
        if (todayBars.Count < 3)
        {
            return false;
        }

        if (!TryReferenceLevels(todayBars, ist, out var h10, out _))
        {
            return false;
        }

        var dayLow = todayBars.Min(b => b.Low);
        var postFade = todayBars.Where(b => IstTime(ist, b.Time).TimeOfDay >= FadeEarliest).ToList();
        for (var i = postFade.Count - 1; i >= 0; i--)
        {
            var bar = postFade[i];
            if (bar.Close >= h10)
            {
                continue;
            }

            var priorMax = todayBars.Where(b => b.Time < bar.Time).Select(b => b.High).DefaultIfEmpty(0).Max();
            if (priorMax < h10 * 0.999m)
            {
                continue;
            }

            var entry = bar.Close;
            var stop = bar.High + StopBufferPoints;
            var risk = stop - entry;
            var reward = entry - dayLow;
            if (risk <= 0 || reward <= 0)
            {
                continue;
            }

            signal = new(
                HighFadeStrategy,
                symbol,
                h10,
                entry,
                stop,
                dayLow,
                bar.Time,
                [
                    $"H10={h10:0.##} (15m 9:15–10:00)",
                    $"15m close below H10 after 11:00 IST",
                    $"stop=break candle high + {StopBufferPoints} pts ({stop:0.##})",
                    $"target day low {dayLow:0.##}",
                    "trail: breakeven then H10 when favorable",
                ]);
            return true;
        }

        return false;
    }

    public static bool TryBuildLowBreak(
        string symbol,
        IReadOnlyList<ChartBar> bars,
        decimal dayOpen,
        DateTimeOffset nowUtc,
        out TenAmSignal? signal)
    {
        signal = null;
        if (bars.Count < 4 || dayOpen <= 0)
        {
            return false;
        }

        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var nowIst = TimeZoneInfo.ConvertTime(nowUtc, ist);
        if (nowIst.TimeOfDay < LowBreakEarliest)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(nowIst.DateTime);
        var todayBars = SessionBars(bars, ist, today);
        if (todayBars.Count < 3)
        {
            return false;
        }

        if (!TryReferenceLevels(todayBars, ist, out _, out var l10))
        {
            return false;
        }

        var referenceBars = todayBars.Where(b => IstTime(ist, b.Time).TimeOfDay <= ReferenceEnd).ToList();
        var negativeMorning = referenceBars.Any(b => b.Close < dayOpen * 0.9995m)
            || referenceBars.LastOrDefault()?.Close < dayOpen;
        if (!negativeMorning)
        {
            return false;
        }

        var postBreak = todayBars.Where(b => IstTime(ist, b.Time).TimeOfDay >= LowBreakEarliest).ToList();
        for (var i = postBreak.Count - 1; i >= 0; i--)
        {
            var bar = postBreak[i];
            if (bar.Close <= l10)
            {
                continue;
            }

            var hadBelow = todayBars.Any(b => b.Time < bar.Time && b.Low <= l10 * 1.001m);
            if (!hadBelow && !todayBars.Any(b => b.Low < l10))
            {
                continue;
            }

            var entry = bar.Close;
            var stop = bar.Low - StopBufferPoints;
            var risk = entry - stop;
            var reward = dayOpen - entry;
            if (risk <= 0 || reward <= 0)
            {
                continue;
            }

            signal = new(
                LowBreakStrategy,
                symbol,
                l10,
                entry,
                stop,
                dayOpen,
                bar.Time,
                [
                    $"L10={l10:0.##} (15m 9:15–10:00)",
                    "negative morning vs day open",
                    $"15m close above L10 after 10:00 IST",
                    $"stop=break candle low − {StopBufferPoints} pts ({stop:0.##})",
                    $"target day open {dayOpen:0.##}",
                    "trail: breakeven then L10 when favorable",
                ]);
            return true;
        }

        return false;
    }

    public static void ApplyTrailingStop(
        string strategy,
        decimal referenceLevel,
        decimal entryPrice,
        decimal lastPrice,
        ref decimal stopLoss,
        bool isShort)
    {
        if (isShort)
        {
            if (lastPrice >= entryPrice)
            {
                return;
            }

            var towardTarget = entryPrice - lastPrice;
            if (towardTarget <= 0)
            {
                return;
            }

            if (stopLoss > entryPrice)
            {
                stopLoss = entryPrice;
            }

            if (towardTarget >= Math.Max(5m, (entryPrice - referenceLevel) * 0.15m))
            {
                stopLoss = Math.Min(stopLoss, referenceLevel);
            }
        }
        else
        {
            if (lastPrice <= entryPrice)
            {
                return;
            }

            var towardTarget = lastPrice - entryPrice;
            if (towardTarget <= 0)
            {
                return;
            }

            if (stopLoss < entryPrice)
            {
                stopLoss = entryPrice;
            }

            if (towardTarget >= Math.Max(5m, (referenceLevel - entryPrice) * 0.15m))
            {
                stopLoss = Math.Max(stopLoss, referenceLevel);
            }
        }
    }

    private static bool TryReferenceLevels(
        IReadOnlyList<ChartBar> todayBars,
        TimeZoneInfo ist,
        out decimal h10,
        out decimal l10)
    {
        h10 = 0;
        l10 = 0;
        var window = todayBars
            .Where(b =>
            {
                var t = IstTime(ist, b.Time).TimeOfDay;
                return t >= SessionStart && t <= ReferenceEnd;
            })
            .ToList();
        if (window.Count == 0)
        {
            return false;
        }

        h10 = window.Max(b => b.High);
        l10 = window.Min(b => b.Low);
        return h10 > 0 && l10 > 0 && h10 >= l10;
    }

    private static List<ChartBar> SessionBars(IReadOnlyList<ChartBar> bars, TimeZoneInfo ist, DateOnly day) =>
        bars.Where(b => IstDate(ist, b.Time) == day).OrderBy(b => b.Time).ToList();

    private static DateTime IstTime(TimeZoneInfo ist, long unixSeconds) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), ist).DateTime;

    private static DateOnly IstDate(TimeZoneInfo ist, long unixSeconds) =>
        DateOnly.FromDateTime(IstTime(ist, unixSeconds));
}
