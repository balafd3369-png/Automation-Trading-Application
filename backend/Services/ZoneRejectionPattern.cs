using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed record ZoneRejectionBacktestResult(
    string Symbol,
    string Pattern,
    int Samples,
    int Wins,
    int Losses,
    decimal WinRatePercent,
    decimal AvgRewardPoints,
    decimal AvgRiskPoints);

public static class ZoneRejectionPattern
{
    public const string B1RejectionToS2 = "STOCK_B1_REJECTION_TO_S2";
    public const decimal MinWinRateToRecommend = 90m;
    public const int MinBacktestSamples = 8;
    private const decimal TouchTolerance = 0.0015m;

    public static ZoneRejectionBacktestResult BacktestB1RejectionToS2(
        string symbol,
        IReadOnlyList<ChartBar> bars,
        decimal b1,
        decimal s2)
    {
        if (bars.Count < 10 || b1 <= 0 || s2 <= 0 || b1 <= s2)
        {
            return new(symbol, B1RejectionToS2, 0, 0, 0, 0, 0, 0);
        }

        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var wins = 0;
        var losses = 0;
        decimal rewardSum = 0;
        decimal riskSum = 0;
        foreach (var session in bars.GroupBy(bar => IstDate(ist, bar.Time)))
        {
            var sessionBars = session.OrderBy(b => b.Time).ToList();
            if (sessionBars.Count < 6)
            {
                continue;
            }

            for (var i = 2; i < sessionBars.Count - 1; i++)
            {
                var priorHigh = sessionBars.Take(i).Max(b => b.High);
                var bar = sessionBars[i];
                if (priorHigh < b1 * (1m - TouchTolerance))
                {
                    continue;
                }

                if (bar.High < b1 * (1m - TouchTolerance) || bar.Close >= b1)
                {
                    continue;
                }

                var entry = bar.Close;
                var stop = Math.Max(bar.High, b1) * (1m + TouchTolerance);
                var risk = stop - entry;
                var reward = entry - s2;
                if (risk <= 0 || reward <= 0)
                {
                    continue;
                }

                var outcome = Simulate(sessionBars, i + 1, entry, stop, s2, bearish: true);
                if (outcome is null)
                {
                    continue;
                }

                if (outcome == true)
                {
                    wins++;
                    rewardSum += entry - s2;
                }
                else
                {
                    losses++;
                    riskSum += stop - entry;
                }
            }
        }

        var samples = wins + losses;
        var winRate = samples == 0 ? 0 : decimal.Round((decimal)wins / samples * 100m, 2);
        return new(
            symbol,
            B1RejectionToS2,
            samples,
            wins,
            losses,
            winRate,
            samples == 0 ? 0 : decimal.Round(rewardSum / Math.Max(1, wins), 2),
            samples == 0 ? 0 : decimal.Round(riskSum / Math.Max(1, losses), 2));
    }

    public static bool QualifiesForUserRecommendation(ZoneRejectionBacktestResult result) =>
        result.Samples >= MinBacktestSamples && result.WinRatePercent >= MinWinRateToRecommend;

    public static bool TryDetectSessionRejection(
        decimal sessionHigh,
        decimal previousLast,
        decimal last,
        decimal b1,
        out bool freshSignal)
    {
        freshSignal = false;
        if (b1 <= 0)
        {
            return false;
        }

        var touch = b1 * (1m - TouchTolerance);
        if (sessionHigh < touch)
        {
            return false;
        }

        var rejectedNow = previousLast >= touch && last < touch;
        freshSignal = rejectedNow;
        return true;
    }

    private static bool? Simulate(
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
        }

        return null;
    }

    private static DateOnly IstDate(TimeZoneInfo ist, long unixSeconds)
    {
        var utc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, ist).DateTime);
    }

    public static bool TryReadZoneFact(IReadOnlyList<string> facts, string prefix, out decimal value)
    {
        value = 0;
        var fact = facts.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
        return fact is not null
            && decimal.TryParse(fact[(fact.IndexOf('=') + 1)..], System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
