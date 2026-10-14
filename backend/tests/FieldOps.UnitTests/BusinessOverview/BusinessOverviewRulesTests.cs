using FieldOps.Application.Features.BusinessOverview;

namespace FieldOps.UnitTests.BusinessOverview;

/// <summary>Period, range, margin, change, conversion and refund-adjusted allocation rules (business-overview BR-03, BR-05, BR-06, BR-09, BR-11, BR-13; AC-05, AC-06, AC-08, AC-10).</summary>
public class BusinessOverviewRulesTests
{
    private static DateOnly D(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd");

    // BR-03: granularity by length, the previous period of the same length ending the day before, clipped contiguous buckets.
    [Theory]
    [InlineData("2026-02-01", "2026-02-28", "day", "2026-01-04", "2026-01-31", 28, "2026-02-01", "2026-02-01", "2026-02-28", "2026-02-28")]
    [InlineData("2026-03-01", "2026-03-31", "day", "2026-01-29", "2026-02-28", 31, "2026-03-01", "2026-03-01", "2026-03-31", "2026-03-31")]
    [InlineData("2026-01-01", "2026-03-01", "week", "2025-11-02", "2025-12-31", 9, "2026-01-01", "2026-01-04", "2026-02-23", "2026-03-01")]
    [InlineData("2026-01-01", "2026-04-10", "month", "2025-09-23", "2025-12-31", 4, "2026-01-01", "2026-01-31", "2026-04-01", "2026-04-10")]
    [InlineData("2024-01-01", "2024-12-31", "month", "2022-12-31", "2023-12-31", 12, "2024-01-01", "2024-01-31", "2024-12-01", "2024-12-31")]
    public void ResolvePeriod_GranularityPreviousPeriodAndClippedBuckets_FollowTheRange(
        string from,
        string to,
        string granularity,
        string previousFrom,
        string previousTo,
        int buckets,
        string firstStart,
        string firstEnd,
        string lastStart,
        string lastEnd)
    {
        var period = BusinessOverviewRules.ResolvePeriod(D(from), D(to));

        Assert.Equal(granularity, period.Granularity);
        Assert.Equal((D(previousFrom), D(previousTo)), (period.PreviousFrom, period.PreviousTo));
        Assert.Equal(period.Days, period.PreviousTo.DayNumber - period.PreviousFrom.DayNumber + 1);
        Assert.Equal(buckets, period.Buckets.Count);
        Assert.Equal((D(firstStart), D(firstEnd)), (period.Buckets[0].Start, period.Buckets[0].End));
        Assert.Equal((D(lastStart), D(lastEnd)), (period.Buckets[^1].Start, period.Buckets[^1].End));
        Assert.Equal(period.Days, period.Buckets.Sum(bucket => bucket.End.DayNumber - bucket.Start.DayNumber + 1));
    }

    // BR-03: reversed, future and longer than 366 days are errors on "to"; exactly 366 days is valid.
    [Theory]
    [InlineData("2026-03-10", "2026-03-09", false)]
    [InlineData("2026-03-01", "2026-03-21", false)]
    [InlineData("2025-03-01", "2026-03-10", false)]
    [InlineData("2025-03-10", "2026-03-10", true)]
    [InlineData("2026-03-10", "2026-03-10", true)]
    public void ValidateRange_ReversedFutureOrTooLong_ReportsTheToField(string from, string to, bool valid)
    {
        var errors = BusinessOverviewRules.ValidateRange(D(from), D(to), D("2026-03-20"));

        Assert.Equal(valid, errors.Count == 0);
        Assert.All(errors.Keys, key => Assert.Equal("to", key));
    }

    // BR-05, BR-09, BR-11, BR-06: margins keep their sign, a zero base has no value, below target needs revenue.
    [Fact]
    public void Margin_ChangeConversionAndTarget_FollowTheRules()
    {
        Assert.Equal(87.5m, BusinessOverviewRules.Margin(175m, 200m));
        Assert.Equal(53.2m, BusinessOverviewRules.Margin(335m, 630m));
        Assert.Equal(-50.0m, BusinessOverviewRules.Margin(-50m, 100m));
        Assert.Null(BusinessOverviewRules.Margin(10m, 0m));

        Assert.True(BusinessOverviewRules.BelowTarget(100m, 39.9m));
        Assert.False(BusinessOverviewRules.BelowTarget(100m, 40.0m));
        Assert.False(BusinessOverviewRules.BelowTarget(0m, -10m));
        Assert.False(BusinessOverviewRules.BelowTarget(100m, null));

        Assert.Equal(12.4m, BusinessOverviewRules.ChangePercent(112.4m, 100m));
        Assert.Equal(-3.1m, BusinessOverviewRules.ChangePercent(96.9m, 100m));
        Assert.Equal(50.0m, BusinessOverviewRules.ChangePercent(-50m, -100m));
        Assert.Equal(0.0m, BusinessOverviewRules.ChangePercent(100m, 100m));
        Assert.Null(BusinessOverviewRules.ChangePercent(10m, 0m));

        Assert.Equal(233, BusinessOverviewRules.ConversionPercent(7, 3));
        Assert.Equal(67, BusinessOverviewRules.ConversionPercent(2, 3));
        Assert.Null(BusinessOverviewRules.ConversionPercent(2, 0));
    }

    // BR-13: the refunded share is removed per allocation and rounded to cents away from zero.
    [Theory]
    [InlineData(100, 100, 0, 100.00)]
    [InlineData(100, 100, 25, 75.00)]
    [InlineData(100, 100, 100, 0.00)]
    [InlineData(10, 30, 10, 6.67)]
    [InlineData(0.05, 1, 0.5, 0.03)]
    public void NetAllocation_RefundShare_IsRoundedPerAllocation(double allocation, double payment, double refunded, double expected) =>
        Assert.Equal((decimal)expected, BusinessOverviewRules.NetAllocation((decimal)allocation, (decimal)payment, (decimal)refunded));
}
