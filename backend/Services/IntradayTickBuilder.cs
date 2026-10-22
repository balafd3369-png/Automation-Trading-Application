using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class IntradayTickBuilder
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    public sealed record Tick(DateTimeOffset TimeIst, decimal Last);

    public static DateTime IstToday()
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist).Date;

    public static void Record(List<Tick> ticks, decimal last)
    {
        if (last <= 0)
        {
            return;
        }

        var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
        if (ticks.Count > 0)
        {
            var firstDay = ticks[0].TimeIst.Date;
            if (firstDay != nowIst.Date)
            {
                ticks.Clear();
            }
        }

        ticks.Add(new Tick(nowIst, last));
        if (ticks.Count > 8000)
        {
            ticks.RemoveRange(0, ticks.Count - 8000);
        }
    }

    public static List<ChartBar> BuildBars(IReadOnlyList<Tick> ticks, string interval, decimal todayOpen)
    {
        if (ticks.Count == 0)
        {
            return [];
        }

        var step = ChartSessionMerger.StepMinutes(interval);
        var today = IstToday();
        var sessionStart = today.AddHours(9).AddMinutes(15);
        var sessionEnd = today.AddHours(15).AddMinutes(30);
        var sessionTicks = ticks
            .Where(t => t.TimeIst.Date == today && t.TimeIst >= sessionStart && t.TimeIst < sessionEnd)
            .OrderBy(t => t.TimeIst)
            .ToList();
        if (sessionTicks.Count == 0)
        {
            return [];
        }

        var grouped = sessionTicks
            .GroupBy(t => ChartSessionMerger.BucketStartIst(t.TimeIst, step).ToUnixTimeSeconds())
            .OrderBy(g => g.Key)
            .ToList();
        var bars = new List<ChartBar>(grouped.Count);
        for (var i = 0; i < grouped.Count; i++)
        {
            var g = grouped[i];
            var rows = g.OrderBy(t => t.TimeIst).ToList();
            var open = i == 0 && todayOpen > 0 ? todayOpen : rows[0].Last;
            var high = Math.Max(open, rows.Max(r => r.Last));
            var low = Math.Min(open, rows.Min(r => r.Last));
            var close = rows[^1].Last;
            bars.Add(new ChartBar(g.Key, open, high, low, close));
        }

        return bars;
    }

    public static void BootstrapFromOpen(List<Tick> ticks, decimal todayOpen, decimal liveLast)
    {
        if (todayOpen <= 0 || liveLast <= 0 || ticks.Count >= 12)
        {
            return;
        }

        var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
        var sessionStart = nowIst.Date.AddHours(9).AddMinutes(15);
        var sessionEnd = nowIst.Date.AddHours(15).AddMinutes(30);
        if (nowIst < sessionStart || nowIst >= sessionEnd)
        {
            return;
        }

        var minutes = Math.Max(1, (int)(nowIst - sessionStart).TotalMinutes);
        ticks.Clear();
        for (var m = 0; m <= minutes; m++)
        {
            var t = sessionStart.AddMinutes(m);
            var frac = (decimal)m / minutes;
            var price = todayOpen + (liveLast - todayOpen) * frac;
            ticks.Add(new Tick(new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), Ist.GetUtcOffset(t)), price));
        }
    }
}
