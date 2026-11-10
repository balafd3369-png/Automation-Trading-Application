using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class ChartCandleDiagnostics
{
    public sealed record Summary(
        int Count,
        long? FirstTime,
        long? LastTime,
        int DuplicateCount,
        int GapCount,
        int InvalidOhlcCount,
        bool Sorted);

    public static Summary Analyze(string interval, IReadOnlyList<ChartBar> candles)
    {
        if (candles.Count == 0)
        {
            return new Summary(0, null, null, 0, 0, 0, true);
        }

        var times = candles.Select(c => c.Time).ToList();
        var sorted = true;
        for (var i = 1; i < times.Count; i++)
        {
            if (times[i] < times[i - 1])
            {
                sorted = false;
                break;
            }
        }

        var duplicateCount = times.Count - times.Distinct().Count();
        var step = ExpectedStepSeconds(interval);
        var gapCount = 0;
        if (step > 0)
        {
            for (var i = 1; i < candles.Count; i++)
            {
                var delta = candles[i].Time - candles[i - 1].Time;
                if (delta > step * 3 / 2)
                {
                    gapCount++;
                }
            }
        }

        var invalid = candles.Count(c => !IsValidOhlc(c));
        return new Summary(
            candles.Count,
            times[0],
            times[^1],
            duplicateCount,
            gapCount,
            invalid,
            sorted);
    }

    public static void Log(
        ILogger logger,
        string symbol,
        string interval,
        string source,
        IReadOnlyList<ChartBar> candles)
    {
        var s = Analyze(interval, candles);
        logger.LogInformation(
            "ChartAsync source={Source} symbol={Symbol} interval={Interval} candleCount={Count} firstTs={First} lastTs={Last} duplicateCount={Dupes} gapCount={Gaps} invalidOhlc={Invalid} sorted={Sorted}",
            source,
            symbol,
            interval,
            s.Count,
            s.FirstTime,
            s.LastTime,
            s.DuplicateCount,
            s.GapCount,
            s.InvalidOhlcCount,
            s.Sorted);
    }

    private static int ExpectedStepSeconds(string interval) =>
        interval switch
        {
            "minute" or "1m" => 60,
            "5minute" => 300,
            "15minute" or "15m" => 900,
            "60minute" or "1h" => 3600,
            "day" or "1D" => 86_400,
            _ => 300,
        };

    private static bool IsValidOhlc(ChartBar c) =>
        c.Open > 0
        && c.High > 0
        && c.Low > 0
        && c.Close > 0
        && c.High >= c.Low
        && c.High >= c.Open
        && c.High >= c.Close
        && c.Low <= c.Open
        && c.Low <= c.Close;
}
