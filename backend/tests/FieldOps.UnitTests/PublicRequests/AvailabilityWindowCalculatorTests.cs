using FieldOps.Application.Features.PublicRequests;

namespace FieldOps.UnitTests.PublicRequests;

public class AvailabilityWindowCalculatorTests
{
    // BR-07, BR-12: window bounds are wall-clock times in the organization time zone.
    [Theory]
    [InlineData("America/Chicago", "2026-07-15", "morning", "2026-07-15T13:00:00Z", "2026-07-15T17:00:00Z")]
    [InlineData("America/Chicago", "2026-01-15", "afternoon", "2026-01-15T18:00:00Z", "2026-01-15T23:00:00Z")]
    [InlineData("America/Chicago", "2026-07-15", "evening", "2026-07-15T22:00:00Z", "2026-07-16T01:00:00Z")]
    [InlineData("UTC", "2026-03-01", "any", "2026-03-01T08:00:00Z", "2026-03-01T20:00:00Z")]
    [InlineData("Pacific/Auckland", "2026-01-10", "morning", "2026-01-09T19:00:00Z", "2026-01-09T23:00:00Z")]
    public void Calculate_ReturnsWindowBoundsInOrganizationTimeZone(
        string zone, string date, string window, string expectedStart, string expectedEnd)
    {
        var (start, end) = AvailabilityWindowCalculator.Calculate(
            DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
            window,
            TimeZoneInfo.FindSystemTimeZoneById(zone));

        Assert.Equal(DateTimeOffset.Parse(expectedStart, System.Globalization.CultureInfo.InvariantCulture), start);
        Assert.Equal(DateTimeOffset.Parse(expectedEnd, System.Globalization.CultureInfo.InvariantCulture), end);
    }

    [Fact]
    public void Today_UsesTheOrganizationTimeZoneAndWindowsAreValidated()
    {
        var instant = DateTimeOffset.Parse("2026-07-16T02:30:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(new DateOnly(2026, 7, 15), AvailabilityWindowCalculator.Today(instant, TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")));
        Assert.Equal(new DateOnly(2026, 7, 16), AvailabilityWindowCalculator.Today(instant, TimeZoneInfo.Utc));
        Assert.True(AvailabilityWindowCalculator.IsValidWindow("any"));
        Assert.False(AvailabilityWindowCalculator.IsValidWindow("night"));
        Assert.False(AvailabilityWindowCalculator.IsValidWindow(null));
    }
}
