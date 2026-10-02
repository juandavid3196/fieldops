namespace FieldOps.Application.Features.PublicRequests;

/// <summary>
/// Maps a preferred date and time window to the stored preferred start/end in
/// the organization time zone (public service request BR-07, BR-12).
/// </summary>
public static class AvailabilityWindowCalculator
{
    public const int MaxDaysAhead = 90;

    private static readonly Dictionary<string, (int StartHour, int EndHour)> Windows = new(StringComparer.Ordinal)
    {
        ["morning"] = (8, 12),
        ["afternoon"] = (12, 17),
        ["evening"] = (17, 20),
        ["any"] = (8, 20),
    };

    public static bool IsValidWindow(string? timeWindow) =>
        timeWindow is not null && Windows.ContainsKey(timeWindow);

    public static DateOnly Today(DateTimeOffset now, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);

    public static (DateTimeOffset Start, DateTimeOffset End) Calculate(
        DateOnly date, string timeWindow, TimeZoneInfo timeZone)
    {
        var (startHour, endHour) = Windows[timeWindow];

        return (ToInstant(date, startHour, timeZone), ToInstant(date, endHour, timeZone));
    }

    private static DateTimeOffset ToInstant(DateOnly date, int hour, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified);

        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }
}
