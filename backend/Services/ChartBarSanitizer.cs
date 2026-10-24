using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class ChartBarSanitizer
{
    /// <summary>Clamp corrupt broker/API wicks so one bad tick cannot paint a vertical line.</summary>
    public static ChartBar ClampWicks(ChartBar bar, string interval)
    {
        var open = bar.Open > 0 ? bar.Open : bar.Close;
        var close = bar.Close > 0 ? bar.Close : open;
        var bodyHigh = Math.Max(open, close);
        var bodyLow = Math.Min(open, close);
        var mid = (bodyHigh + bodyLow) / 2m;
        if (mid <= 0)
        {
            return bar;
        }

        var maxPct = interval switch
        {
            "minute" => 0.0035m,
            "15minute" => 0.01m,
            "60minute" => 0.02m,
            "day" => 0.05m,
            _ => 0.007m,
        };
        var limit = Math.Max(mid * maxPct, 2m);
        var high = Math.Min(bar.High > 0 ? bar.High : bodyHigh, bodyHigh + limit);
        var low = Math.Max(bar.Low > 0 ? bar.Low : bodyLow, bodyLow - limit);
        if (high <= low)
        {
            high = bodyHigh + 0.05m;
            low = bodyLow - 0.05m;
        }

        return new ChartBar(bar.Time, open, high, low, close);
    }

    public static List<ChartBar> SanitizeIntraday(
        IReadOnlyList<ChartBar> bars,
        string interval,
        decimal liveLast)
    {
        var clean = new List<ChartBar>(bars.Count);
        foreach (var bar in bars.OrderBy(b => b.Time))
        {
            clean.Add(ClampWicks(bar, interval));
        }

        if (clean.Count == 0 || liveLast <= 0)
        {
            return clean;
        }

        if (!NseCashSession.IsLiveIntradayWindowNow())
        {
            return clean;
        }

        var tail = clean[^1];
        if (NseCashSession.BarIstDate(tail.Time) != NseCashSession.IstToday())
        {
            return clean;
        }

        var patched = ClampWicks(
            tail with
            {
                Close = liveLast,
                High = Math.Max(tail.High, liveLast),
                Low = Math.Min(tail.Low, liveLast),
            },
            interval);
        clean[^1] = patched;
        return clean;
    }
}
