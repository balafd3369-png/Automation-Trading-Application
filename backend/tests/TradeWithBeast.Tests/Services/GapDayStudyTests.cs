using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class GapDayStudyTests
{
    [Fact]
    public void DistributionDay_FollowedByGapDown_RecordedInClue()
    {
        var bars = SyntheticFiveMinuteHistory();
        var result = GapDayStudy.Analyze("TEST", bars);
        Assert.True(result.Clues.TryGetValue(GapDayStudy.ClueDistributionGapDown, out var dist));
        Assert.True(dist.Events >= 1);
    }

    private static List<ChartBar> SyntheticFiveMinuteHistory()
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var start = new DateTime(2026, 1, 6, 9, 15, 0);
        var bars = new List<ChartBar>();
        decimal price = 1000m;

        for (var day = 0; day < 12; day++)
        {
            var session = start.AddDays(day);
            if (session.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var open = price;
            var high = price;
            var low = price;
            var close = price;

            for (var slot = 0; slot < 12; slot++)
            {
                var t = session.AddMinutes(5 * slot);
                var utc = TimeZoneInfo.ConvertTimeToUtc(t, ist);
                var unix = new DateTimeOffset(utc).ToUnixTimeSeconds();

                if (day == 8 && slot == 0)
                {
                    open = price;
                    high = price + 30;
                    low = price;
                    close = price + 25;
                }
                else if (day == 8)
                {
                    high = Math.Max(high, price + 30 - slot);
                    low = Math.Min(low, price + 15 - slot * 2);
                    close = price + 10 - slot * 3;
                }
                else if (day == 9 && slot == 0)
                {
                    open = close - 15;
                    high = open;
                    low = open - 8;
                    close = open - 6;
                }
                else if (day == 9)
                {
                    low = Math.Min(low, open - 8 - slot);
                    close = open - 6 - slot;
                    high = open;
                }
                else
                {
                    close = price + slot * 0.5m;
                    high = close + 1;
                    low = close - 1;
                }

                bars.Add(new ChartBar(unix, open, high, low, close));
            }

            price = close;
        }

        return bars;
    }
}
