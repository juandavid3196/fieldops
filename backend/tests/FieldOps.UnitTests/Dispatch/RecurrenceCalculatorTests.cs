using FieldOps.Application.Features.Dispatch;

namespace FieldOps.UnitTests.Dispatch;

/// <summary>BR-18 next-occurrence calculation: intervals, month clamping and DST in the branch zone.</summary>
public class RecurrenceCalculatorTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static (DateTimeOffset Start, DateTimeOffset End) Next(string frequency, DateOnly date, TimeZoneInfo zone) =>
        RecurrenceCalculator.NextWindow(frequency, date, new TimeOnly(9, 0), new TimeOnly(11, 0), zone)!.Value;

    // Intervals and clamping: weekly 7 days, biweekly 14, monthly and quarterly end on the last day of a shorter month.
    [Theory]
    [InlineData("weekly", "2030-01-02", "2030-01-09")]
    [InlineData("biweekly", "2030-01-02", "2030-01-16")]
    [InlineData("monthly", "2030-01-31", "2030-02-28")]
    [InlineData("monthly", "2028-01-31", "2028-02-29")]
    [InlineData("monthly", "2030-12-15", "2031-01-15")]
    [InlineData("quarterly", "2030-01-31", "2030-04-30")]
    [InlineData("quarterly", "2030-11-30", "2031-02-28")]
    public void NextWindow_AddsTheIntervalAndClampsToTheLastDayOfTheMonth(string frequency, string from, string expectedDate)
    {
        var (start, end) = Next(frequency, DateOnly.Parse(from), TimeZoneInfo.Utc);

        Assert.Equal(DateTimeOffset.Parse($"{expectedDate}T09:00:00Z"), start);
        Assert.Equal(DateTimeOffset.Parse($"{expectedDate}T11:00:00Z"), end);
    }

    // The local window time is kept across DST: the UTC offset follows the zone, a skipped time moves an hour later, a repeated one is the first.
    [Theory]
    [InlineData("2030-03-08", "weekly", "2030-03-15T13:00:00Z")]
    [InlineData("2030-11-01", "weekly", "2030-11-08T14:00:00Z")]
    [InlineData("2030-01-31", "quarterly", "2030-04-30T13:00:00Z")]
    public void NextWindow_KeepsTheLocalTimeAcrossDaylightSavingChanges(string from, string frequency, string expectedUtc)
    {
        var (start, _) = Next(frequency, DateOnly.Parse(from), NewYork);

        Assert.Equal(DateTimeOffset.Parse(expectedUtc), start);
    }

    [Fact]
    public void ToInstant_HandlesSkippedAndRepeatedLocalTimes_AndNoWindowForAnEmptyOrUnknownInterval()
    {
        // 2030-03-10 02:30 does not exist in New York; 2030-11-03 01:30 happens twice.
        Assert.Equal(
            DateTimeOffset.Parse("2030-03-10T06:30:00Z"),
            RecurrenceCalculator.ToInstant(new DateOnly(2030, 3, 10), new TimeOnly(2, 30), NewYork));
        Assert.Equal(
            DateTimeOffset.Parse("2030-11-03T05:30:00Z"),
            RecurrenceCalculator.ToInstant(new DateOnly(2030, 11, 3), new TimeOnly(1, 30), NewYork));
        Assert.Null(RecurrenceCalculator.NextWindow("weekly", new DateOnly(2030, 1, 2), new TimeOnly(9, 0), new TimeOnly(9, 0), TimeZoneInfo.Utc));
        Assert.Null(RecurrenceCalculator.NextWindow("daily", new DateOnly(2030, 1, 2), new TimeOnly(9, 0), new TimeOnly(11, 0), TimeZoneInfo.Utc));
    }
}
