namespace Debits.Application.Common;

/// <summary>Shop-local calendar helpers. All timestamps are stored in UTC; due dates and reports are in the shop's zone.</summary>
public static class LocalTime
{
    public static TimeZoneInfo Zone(string timezone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static bool IsValidZone(string timezone)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static DateOnly Today(TimeProvider clock, string timezone) => ToLocalDate(clock.GetUtcNow().UtcDateTime, timezone);

    public static DateOnly ToLocalDate(DateTime utc, string timezone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone(timezone)));

    public static DateTime StartOfDayUtc(DateOnly date, string timezone) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone(timezone));

    /// <summary>[from 00:00, to+1 00:00) in UTC for an inclusive local date range.</summary>
    public static (DateTime FromUtc, DateTime ToUtcExclusive) UtcRange(DateOnly from, DateOnly toInclusive, string timezone) =>
        (StartOfDayUtc(from, timezone), StartOfDayUtc(toInclusive.AddDays(1), timezone));

    public static (DateOnly From, DateOnly To) Month(DateOnly anyDay)
    {
        var from = new DateOnly(anyDay.Year, anyDay.Month, 1);
        return (from, from.AddMonths(1).AddDays(-1));
    }
}
