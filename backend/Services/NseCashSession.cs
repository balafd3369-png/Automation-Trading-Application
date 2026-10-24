using System.Text.Json;

namespace TradeWithBeast.Services;

/// <summary>NSE/BSE cash equity session: weekdays, exchange holidays, 09:15–15:30 IST.</summary>
public static class NseCashSession
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
    private static readonly HashSet<DateOnly> Holidays = LoadHolidays();
    private const int SessionOpenMinutes = 9 * 60 + 15;
    private const int SessionCloseMinutes = 15 * 60 + 30;

    public static DateOnly IstToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist).Date);

    public static DateTimeOffset IstNow() =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);

    public static bool IsTradingDay(DateOnly istDate) =>
        istDate.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !Holidays.Contains(istDate);

    public static bool IsTradingDay(DateTime istCalendarDate) =>
        IsTradingDay(DateOnly.FromDateTime(istCalendarDate));

    /// <summary>True only on a trading day during regular cash market hours.</summary>
    public static bool IsLiveIntradayWindow(DateTimeOffset timeIst)
    {
        if (!IsTradingDay(DateOnly.FromDateTime(timeIst.Date)))
        {
            return false;
        }

        var minutes = timeIst.Hour * 60 + timeIst.Minute;
        return minutes >= SessionOpenMinutes && minutes < SessionCloseMinutes;
    }

    public static bool IsLiveIntradayWindowNow() => IsLiveIntradayWindow(IstNow());

    public static DateOnly BarIstDate(long unixSeconds)
    {
        var ist = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), Ist);
        return DateOnly.FromDateTime(ist.Date);
    }

    private static HashSet<DateOnly> LoadHolidays()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "data", "nse-trading-holidays.json");
            if (!File.Exists(path))
            {
                path = Path.Combine(Directory.GetCurrentDirectory(), "data", "nse-trading-holidays.json");
            }

            if (!File.Exists(path))
            {
                return [];
            }

            var json = File.ReadAllText(path);
            var dates = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return dates
                .Select(d => DateOnly.Parse(d, System.Globalization.CultureInfo.InvariantCulture))
                .ToHashSet();
        }
        catch
        {
            return [];
        }
    }
}
