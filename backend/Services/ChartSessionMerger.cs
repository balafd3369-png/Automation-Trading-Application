using System.Globalization;
using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class ChartSessionMerger
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    public static DateTime IstToday()
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist).Date;

    public static DateTimeOffset IstNow()
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);

    /// <summary>NSE/BSE cash session 09:15–15:30 IST (exclusive end).</summary>
    public static bool IsCashSessionOpen(DateTimeOffset timeIst)
    {
        var minutes = timeIst.Hour * 60 + timeIst.Minute;
        var open = 9 * 60 + 15;
        var close = 15 * 60 + 30;
        return minutes >= open && minutes < close;
    }

    public static DateTimeOffset SessionEndIst(DateTime istDate)
    {
        var local = istDate.AddHours(15).AddMinutes(30);
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Ist.GetUtcOffset(local));
    }

    public static DateTimeOffset SessionStartIst(DateTime istDate)
    {
        var local = istDate.AddHours(9).AddMinutes(15);
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Ist.GetUtcOffset(local));
    }

    public static DateTimeOffset ParseIst(string raw)
    {
        var text = raw.Trim();
        var formats = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd H:mm:ss" };
        if (!DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
        }

        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, Ist.GetUtcOffset(unspecified));
    }

    public static List<ChartBar> ToBars(IReadOnlyList<DayCandle> candles) =>
        candles
            .GroupBy(c => c.Time.ToUnixTimeSeconds())
            .Select(g => g.Last())
            .OrderBy(c => c.Time)
            .Select(c => new ChartBar(c.Time.ToUnixTimeSeconds(), c.Open, c.High, c.Low, c.Close))
            .ToList();

    public static bool LastBarIsToday(IReadOnlyList<ChartBar> bars)
    {
        if (bars.Count == 0)
        {
            return false;
        }

        var last = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(bars[^1].Time), Ist).Date;
        return last >= IstToday();
    }

    public static int StepMinutes(string interval) =>
        interval switch
        {
            "minute" => 1,
            "5minute" => 5,
            "15minute" => 15,
            "60minute" => 60,
            _ => 5,
        };

    public static DateTimeOffset BucketStartIst(DateTimeOffset timeIst, int stepMinutes)
    {
        var sessionStart = timeIst.Date.AddHours(9).AddMinutes(15);
        var minutesFromSession = Math.Max(0, (int)(timeIst - sessionStart).TotalMinutes);
        var bucketIndex = minutesFromSession / stepMinutes;
        var bucketLocal = sessionStart.AddMinutes(bucketIndex * stepMinutes);
        return new DateTimeOffset(bucketLocal, Ist.GetUtcOffset(bucketLocal));
    }

    public static List<ChartBar> ResampleToday(IReadOnlyList<DayCandle> minuteCandles, string interval)
    {
        var today = IstToday();
        var step = StepMinutes(interval);
        var todayMinute = minuteCandles
            .Where(c => TimeZoneInfo.ConvertTime(c.Time, Ist).Date == today)
            .OrderBy(c => c.Time)
            .ToList();
        if (todayMinute.Count == 0)
        {
            return [];
        }

        return todayMinute
            .GroupBy(c => BucketStartIst(TimeZoneInfo.ConvertTime(c.Time, Ist), step).ToUnixTimeSeconds())
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var rows = g.OrderBy(c => c.Time).ToList();
                return new ChartBar(
                    g.Key,
                    rows[0].Open,
                    rows.Max(r => r.High),
                    rows.Min(r => r.Low),
                    rows[^1].Close);
            })
            .ToList();
    }

    /// <summary>Completed + forming intraday buckets from 09:15 IST through <paramref name="nowIst"/>.</summary>
    public static int MinExpectedTodayBars(DateTimeOffset nowIst, string interval)
    {
        var sessionStart = SessionStartIst(nowIst.Date);
        if (nowIst < sessionStart)
        {
            return 0;
        }

        var sessionEnd = SessionEndIst(nowIst.Date);
        var effective = nowIst < sessionEnd ? nowIst : sessionEnd;
        var step = StepMinutes(interval);
        var minutes = (int)(effective - sessionStart).TotalMinutes;
        if (minutes < 0)
        {
            return 0;
        }

        return (minutes / step) + 1;
    }

    public static List<ChartBar> MergeHistoricalWithToday(IReadOnlyList<ChartBar> historical, IReadOnlyList<ChartBar> todayBars)
    {
        var today = IstToday();
        var merged = historical
            .Where(b => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(b.Time), Ist).Date < today)
            .ToList();
        merged.AddRange(todayBars);
        return merged.OrderBy(b => b.Time).ToList();
    }

    /// <summary>
    /// Before 09:15 IST, drop same-calendar-day intraday bars (avoids phantom "today" candles after midnight).
    /// </summary>
    public static List<ChartBar> TrimIntradayBarsBeforeSessionOpen(IReadOnlyList<ChartBar> bars)
    {
        if (bars.Count == 0)
        {
            return [];
        }

        var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
        var sessionStart = nowIst.Date.AddHours(9).AddMinutes(15);
        if (nowIst >= sessionStart)
        {
            return bars.ToList();
        }

        var today = nowIst.Date;
        return bars
            .Where(b => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(b.Time), Ist).Date < today)
            .ToList();
    }

    public static List<ChartBar> BuildCurrentBar(SymbolSnapshot snap, string interval, decimal liveLast)
    {
        if (liveLast <= 0)
        {
            return [];
        }

        var nowIst = IstNow();
        if (!IsCashSessionOpen(nowIst))
        {
            return [];
        }

        var step = StepMinutes(interval);
        var bucket = BucketStartIst(nowIst, step);
        var open = snap.TodayOpen > 0 ? snap.TodayOpen : liveLast;
        var high = Math.Max(open, liveLast);
        var low = Math.Min(open, liveLast);
        return [new ChartBar(bucket.ToUnixTimeSeconds(), open, high, low, liveLast)];
    }
}
