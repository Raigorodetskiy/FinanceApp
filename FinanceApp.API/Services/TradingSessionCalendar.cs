using FinanceApp.Core.Models;

namespace FinanceApp.API.Services;

internal readonly record struct TradingSessionSpec(
    string TimeZoneId,
    TimeSpan OpenLocalTime,
    TimeSpan CloseLocalTime,
    TradingHolidayCalendar HolidayCalendar);

internal readonly record struct TradingSessionWindow(
    DateOnly SessionDateLocal,
    DateTime SessionStartUtc,
    DateTime SessionEndUtc);

internal enum TradingHolidayCalendar
{
    None = 0,
    UsEquities = 1,
    GermanyXetra = 2,
}

internal static class TradingSessionCalendar
{
    public static bool TryGetSessionSpec(string? exchange, out TradingSessionSpec spec)
    {
        if (!StockExchanges.TryNormalize(exchange, out var normalizedExchange))
        {
            spec = default;
            return false;
        }

        spec = normalizedExchange switch
        {
            StockExchanges.Frankfurt => new TradingSessionSpec(
                "Europe/Berlin",
                TimeSpan.FromHours(9),
                TimeSpan.FromHours(17.5),
                TradingHolidayCalendar.GermanyXetra),
            StockExchanges.Nyse or StockExchanges.Nasdaq => new TradingSessionSpec(
                "America/New_York",
                TimeSpan.FromHours(9.5),
                TimeSpan.FromHours(16),
                TradingHolidayCalendar.UsEquities),
            _ => default
        };

        return !string.IsNullOrWhiteSpace(spec.TimeZoneId);
    }

    public static TimeZoneInfo? TryResolveTimeZone(TradingSessionSpec spec)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(spec.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    public static DateOnly ConvertUtcToLocalDate(DateTime utcTimestamp, TimeZoneInfo timeZone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcTimestamp, timeZone));

    public static bool IsTradingDay(DateOnly date, TradingSessionSpec spec)
    {
        var day = date.DayOfWeek;
        if (day is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        return !IsHoliday(date, spec.HolidayCalendar);
    }

    public static DateOnly GetPreviousTradingDay(DateOnly date, TradingSessionSpec spec)
    {
        var probe = date.AddDays(-1);
        while (!IsTradingDay(probe, spec))
        {
            probe = probe.AddDays(-1);
        }

        return probe;
    }

    public static DateOnly GetNextTradingDay(DateOnly date, TradingSessionSpec spec)
    {
        var probe = date;
        while (!IsTradingDay(probe, spec))
        {
            probe = probe.AddDays(1);
        }

        return probe;
    }

    public static TradingSessionWindow BuildSessionWindow(
        DateOnly sessionDateLocal,
        TradingSessionSpec spec,
        TimeZoneInfo timeZone)
    {
        var startUtc = ConvertLocalBoundaryToUtc(sessionDateLocal, spec.OpenLocalTime, timeZone);
        var endUtc = ConvertLocalBoundaryToUtc(sessionDateLocal, spec.CloseLocalTime, timeZone);
        return new TradingSessionWindow(sessionDateLocal, startUtc, endUtc);
    }

    public static bool IsWithinRegularSession(DateTime utcTimestamp, TradingSessionSpec spec, TimeZoneInfo timeZone, out DateOnly localDate)
    {
        var localTimestamp = TimeZoneInfo.ConvertTimeFromUtc(utcTimestamp, timeZone);
        localDate = DateOnly.FromDateTime(localTimestamp);
        if (!IsTradingDay(localDate, spec))
        {
            return false;
        }

        var localTime = localTimestamp.TimeOfDay;
        return localTime >= spec.OpenLocalTime && localTime <= spec.CloseLocalTime;
    }

    private static DateTime ConvertLocalBoundaryToUtc(DateOnly date, TimeSpan timeOfDay, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.FromTimeSpan(timeOfDay), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private static bool IsHoliday(DateOnly date, TradingHolidayCalendar calendar)
        => calendar switch
        {
            TradingHolidayCalendar.UsEquities => IsUsEquitiesHoliday(date),
            TradingHolidayCalendar.GermanyXetra => IsGermanyXetraHoliday(date),
            _ => false
        };

    private static bool IsUsEquitiesHoliday(DateOnly date)
    {
        var year = date.Year;
        var observedNewYear = ObserveFixedHoliday(new DateOnly(year, 1, 1));
        var mlkDay = NthWeekdayOfMonth(year, 1, DayOfWeek.Monday, 3);
        var presidentsDay = NthWeekdayOfMonth(year, 2, DayOfWeek.Monday, 3);
        var goodFriday = CalculateEasterSunday(year).AddDays(-2);
        var memorialDay = LastWeekdayOfMonth(year, 5, DayOfWeek.Monday);
        var observedJuneteenth = ObserveFixedHoliday(new DateOnly(year, 6, 19));
        var observedIndependenceDay = ObserveFixedHoliday(new DateOnly(year, 7, 4));
        var laborDay = NthWeekdayOfMonth(year, 9, DayOfWeek.Monday, 1);
        var thanksgiving = NthWeekdayOfMonth(year, 11, DayOfWeek.Thursday, 4);
        var observedChristmas = ObserveFixedHoliday(new DateOnly(year, 12, 25));

        return date == observedNewYear
            || date == mlkDay
            || date == presidentsDay
            || date == goodFriday
            || date == memorialDay
            || date == observedJuneteenth
            || date == observedIndependenceDay
            || date == laborDay
            || date == thanksgiving
            || date == observedChristmas;
    }

    private static bool IsGermanyXetraHoliday(DateOnly date)
    {
        var year = date.Year;
        var easterSunday = CalculateEasterSunday(year);

        return date == new DateOnly(year, 1, 1)
            || date == easterSunday.AddDays(-2) // Good Friday
            || date == easterSunday.AddDays(1)  // Easter Monday
            || date == new DateOnly(year, 5, 1) // Labour Day
            || date == new DateOnly(year, 12, 25)
            || date == new DateOnly(year, 12, 26);
    }

    private static DateOnly ObserveFixedHoliday(DateOnly holiday)
        => holiday.DayOfWeek switch
        {
            DayOfWeek.Saturday => holiday.AddDays(-1),
            DayOfWeek.Sunday => holiday.AddDays(1),
            _ => holiday
        };

    private static DateOnly NthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int occurrence)
    {
        var first = new DateOnly(year, month, 1);
        var delta = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(delta + (occurrence - 1) * 7);
    }

    private static DateOnly LastWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek)
    {
        var probe = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        while (probe.DayOfWeek != dayOfWeek)
        {
            probe = probe.AddDays(-1);
        }

        return probe;
    }

    private static DateOnly CalculateEasterSunday(int year)
    {
        // Anonymous Gregorian algorithm.
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
