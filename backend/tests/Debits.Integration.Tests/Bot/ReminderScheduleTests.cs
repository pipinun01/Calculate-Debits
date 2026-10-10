using Debits.Bot.Reminders;

namespace Debits.Integration.Tests.Bot;

public class ReminderScheduleTests
{
    private const string Tashkent = "Asia/Tashkent"; // UTC+5, no DST

    private static DateTime Utc(int day, int hour, int minute, int second = 0) => new(2026, 10, day, hour, minute, second, DateTimeKind.Utc);

    [Theory]
    [InlineData(9, 18, 59, 9, 9)]   // 23:59 local on the 9th: still the 9th
    [InlineData(9, 19, 0, 9, null)] // 00:00 local on the 10th: new day, too early
    [InlineData(10, 3, 59, 9, null)] // 08:59 local
    [InlineData(10, 4, 0, 9, 10)]   // 09:00 local sharp
    [InlineData(10, 12, 0, 9, 10)]  // 17:00 local: late start still catches up
    [InlineData(9, 19, 0, 0, 10)]   // reminder hour 0 fires right at local midnight
    [InlineData(10, 17, 59, 23, null)] // 22:59 local with a 23:00 reminder
    [InlineData(10, 18, 0, 23, 10)]
    public void DueLocalDate_AroundMidnightAndReminderHour(int day, int hour, int minute, int reminderHour, int? expectedDay)
    {
        var due = ReminderSchedule.DueLocalDate(Utc(day, hour, minute), Tashkent, reminderHour);
        due.Should().Be(expectedDay is { } d ? new DateOnly(2026, 10, d) : null);
    }

    [Fact]
    public void DueLocalDate_UnknownZone_FallsBackToUtc()
    {
        ReminderSchedule.DueLocalDate(Utc(10, 8, 59), "Mars/Olympus", 9).Should().BeNull();
        ReminderSchedule.DueLocalDate(Utc(10, 9, 0), "Mars/Olympus", 9).Should().Be(new DateOnly(2026, 10, 10));
    }

    [Fact]
    public void FireTimeUtc_IsTheReminderHourInTheShopZone()
    {
        ReminderSchedule.FireTimeUtc(new DateOnly(2026, 10, 10), Tashkent, 9).Should().Be(Utc(10, 4, 0));
        ReminderSchedule.FireTimeUtc(new DateOnly(2026, 10, 10), Tashkent, 0).Should().Be(Utc(9, 19, 0));
    }

    [Fact]
    public void DelayToNextTick_LandsJustAfterTheNextFiveMinuteBoundary()
    {
        ReminderSchedule.DelayToNextTick(Utc(10, 3, 58, 20)).Should().Be(TimeSpan.FromSeconds(101));
        ReminderSchedule.DelayToNextTick(Utc(10, 4, 0)).Should().Be(TimeSpan.FromSeconds(301));
        ReminderSchedule.DelayToNextTick(Utc(9, 23, 59, 59)).Should().Be(TimeSpan.FromSeconds(2));
    }
}
