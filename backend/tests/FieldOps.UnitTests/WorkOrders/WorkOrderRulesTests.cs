using FieldOps.Application.Features.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class WorkOrderRulesTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    // AC-19: the window is local to the branch zone, "any" spans the local day (23 hours on a DST day) and today follows the zone.
    [Fact]
    public void Window_ConvertsLocalDateAndWindowToInstantsInTheBranchZone()
    {
        var now = new DateTimeOffset(2026, 3, 8, 3, 0, 0, TimeSpan.Zero);

        // 03:00 UTC on 8 March is still 7 March in New York (22:00 EST): today is the 7th.
        Assert.False(WorkOrderWindow.TryResolve(new DateOnly(2026, 3, 6), "09-12", NewYork, now, out _, out _));
        Assert.True(WorkOrderWindow.TryResolve(new DateOnly(2026, 3, 7), "09-12", NewYork, now, out var start, out var end));
        Assert.Equal(new DateTimeOffset(2026, 3, 7, 14, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 3, 7, 17, 0, 0, TimeSpan.Zero), end);
        Assert.Equal((new DateOnly(2026, 3, 7), "09-12"), WorkOrderWindow.Read(start, end, NewYork));

        // The DST day: 9:00 local is EDT (13:00 UTC) and "any" lasts 23 hours.
        Assert.True(WorkOrderWindow.TryResolve(new DateOnly(2026, 3, 8), "09-12", NewYork, now, out start, out end));
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 13, 0, 0, TimeSpan.Zero), start);
        Assert.True(WorkOrderWindow.TryResolve(new DateOnly(2026, 3, 8), WorkOrderWindow.Any, NewYork, now, out start, out end));
        Assert.Equal(TimeSpan.FromHours(23), end - start);
        Assert.Equal((new DateOnly(2026, 3, 8), WorkOrderWindow.Any), WorkOrderWindow.Read(start, end, NewYork));
        Assert.False(WorkOrderWindow.TryResolve(new DateOnly(2026, 3, 8), "10-13", NewYork, now, out _, out _));
    }

    // AC-18: a one-time job has no recurrence and a recurring one needs a frequency and 2 to 24 occurrences.
    [Theory]
    [InlineData("one_time", null, null, false)]
    [InlineData("one_time", "monthly", 6, true)]
    [InlineData("recurring", null, null, true)]
    [InlineData("recurring", "monthly", 1, true)]
    [InlineData("recurring", "monthly", 25, true)]
    [InlineData("recurring", "daily", 5, true)]
    [InlineData("recurring", "monthly", 6, false)]
    [InlineData("recurring", "quarterly", 24, false)]
    public void Recurrence_MustMatchTheJobType(string jobType, string? frequency, int? count, bool invalid)
    {
        var errors = new Dictionary<string, string[]>();
        var recurrence = frequency is null && count is null ? null : new WorkOrderRecurrenceText(frequency, count);

        var (storedFrequency, storedCount) = WorkOrderValidator.ValidateRecurrence(jobType, recurrence, errors);

        Assert.Equal(invalid, errors.ContainsKey("recurrence"));
        Assert.Equal(invalid || jobType == "one_time" ? (null, null) : (frequency, (short?)count), (storedFrequency, storedCount));
    }
}
