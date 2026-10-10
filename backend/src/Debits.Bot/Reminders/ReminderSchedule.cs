using Debits.Application.Common;

namespace Debits.Bot.Reminders;

/// <summary>When a shop's daily reminders go out. Pure functions over UTC instants and the shop's zone.</summary>
public static class ReminderSchedule
{
    /// <summary>The background loop wakes up on these wall-clock boundaries, so an hourly reminder is at most a second late.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromMinutes(5);

    /// <summary>Shop-local date whose reminders are due at <paramref name="utcNow"/>; null before the reminder hour.</summary>
    /// <remarks>Stays non-null until local midnight, which is how a late start catches up on the same day.</remarks>
    public static DateOnly? DueLocalDate(DateTime utcNow, string timezone, int reminderHour)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), LocalTime.Zone(timezone));
        return local.Hour >= reminderHour ? DateOnly.FromDateTime(local) : null;
    }

    /// <summary>UTC instant of the reminder hour on <paramref name="localDate"/>.</summary>
    public static DateTime FireTimeUtc(DateOnly localDate, string timezone, int reminderHour) =>
        LocalTime.StartOfDayUtc(localDate, timezone).AddHours(reminderHour);

    /// <summary>Time to sleep until the next <see cref="Tick"/> boundary plus a second of slack.</summary>
    public static TimeSpan DelayToNextTick(DateTime utcNow)
    {
        var next = new DateTime((utcNow.Ticks / Tick.Ticks + 1) * Tick.Ticks, DateTimeKind.Utc);
        return next - utcNow + TimeSpan.FromSeconds(1);
    }
}
