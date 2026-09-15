using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class MarketSession
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
    private static readonly TimeSpan Open = new(9, 15, 0);
    private static readonly TimeSpan EntryCutoff = new(15, 10, 0);
    private static readonly TimeSpan Close = new(15, 30, 0);

    public static MarketSessionSnapshot Evaluate(DateTimeOffset nowUtc)
    {
        var ist = TimeZoneInfo.ConvertTime(nowUtc, Ist);
        if (IsWeekend(ist))
        {
            return new(
                "WEEKEND",
                false,
                "Markets are closed for the weekend.",
                NextTradingOpenAfter(ist));
        }

        var time = ist.TimeOfDay;
        if (time < Open)
        {
            return new(
                "PRE_OPEN",
                false,
                "Market opens at 09:15 IST.",
                ToIstOffset(ist.Date.Add(Open)));
        }

        if (time >= Close)
        {
            return new(
                "CLOSED",
                false,
                "Market closed for the day.",
                NextTradingOpenAfter(ist));
        }

        if (time >= EntryCutoff)
        {
            return new(
                "SQUARE_OFF",
                false,
                "New live entries are blocked after 15:10 IST. Exits and square-off only.",
                ToIstOffset(ist.Date.Add(Close)));
        }

        return new(
            "OPEN",
            true,
            "Market is open for live entries until 15:10 IST.",
            ToIstOffset(ist.Date.Add(EntryCutoff)));
    }

    private static bool IsWeekend(DateTimeOffset ist) =>
        ist.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static DateTimeOffset NextTradingOpenAfter(DateTimeOffset ist)
    {
        var date = ist.Date;
        if (!IsWeekend(ist) && ist.TimeOfDay < Open)
        {
            return ToIstOffset(date.Add(Open));
        }

        date = date.AddDays(1);
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        return ToIstOffset(date.Add(Open));
    }

    private static DateTimeOffset ToIstOffset(DateTime localDateTime)
    {
        var offset = Ist.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset);
    }
}
