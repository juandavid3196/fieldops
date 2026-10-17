using FieldOps.Application.Features.Team;
using FieldOps.Application.Features.TechnicianVisits;

namespace FieldOps.UnitTests.TechnicianVisits;

/// <summary>Range and day bucketing of the time summary (technician-time-summary BR-02 to BR-04).</summary>
public class TechnicianTimeRulesTests
{
    [Theory]
    [InlineData("day", "2026-03-05", "2026-03-05", "2026-03-05")]
    [InlineData("week", "2026-03-05", "2026-03-02", "2026-03-08")]
    [InlineData("week", "2026-03-08", "2026-03-02", "2026-03-08")]
    [InlineData("week", "2026-03-09", "2026-03-09", "2026-03-15")]
    public void Range_IsTheDayOrTheMondayToSundayContainingTheDate(string view, string date, string first, string last)
    {
        var (from, to) = TechnicianTimeRules.Range(view, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(first, TechnicianTimeRules.Format(from));
        Assert.Equal(last, TechnicianTimeRules.Format(to));
    }

    [Fact]
    public void Bucket_CountsEachEntryWhollyOnTheLocalDayOfItsStartAndSumsTypes()
    {
        var zone = BranchTime.FindZone("America/New_York");
        var (days, totals) = TechnicianTimeRules.Bucket(
            new DateOnly(2026, 3, 2),
            new DateOnly(2026, 3, 8),
            [
                (DateTimeOffset.Parse("2026-03-04T04:50:00Z", System.Globalization.CultureInfo.InvariantCulture), "work", 1800),
                (DateTimeOffset.Parse("2026-03-09T03:59:00Z", System.Globalization.CultureInfo.InvariantCulture), "travel", 60),
                (DateTimeOffset.Parse("2026-03-08T05:30:00Z", System.Globalization.CultureInfo.InvariantCulture), "break", 120),
            ],
            zone);

        Assert.Equal(7, days.Count);
        Assert.Equal(1800, days[1].WorkSeconds);
        Assert.Equal(1800, days[1].TotalSeconds);
        Assert.Equal(0, days[2].TotalSeconds);
        Assert.Equal(180, days[6].TotalSeconds);
        Assert.Equal(new TimeTotals(1800, 60, 120, 1980), totals);
    }
}
