using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.WorkOrders;

/// <summary>
/// The preferred date and arrival window of a work order (create-work-order BR-14): pure conversion between the
/// local date plus window code and the stored instants, in the time zone of the work order branch.
/// </summary>
public static class WorkOrderWindow
{
    public const string Any = "any";

    private static readonly (string Code, int StartHour, int EndHour)[] Windows =
    [
        ("08-11", 8, 11),
        ("09-12", 9, 12),
        ("12-15", 12, 15),
        ("13-16", 13, 16),
        ("15-18", 15, 18),
    ];

    public static bool IsValid(string? code) =>
        code == Any || Windows.Any(window => window.Code == code);

    /// <summary>
    /// The instants of the local date and window ("any" = local midnight to the next local midnight). False when
    /// the date is before today in the zone or the code is unknown.
    /// </summary>
    public static bool TryResolve(
        DateOnly date,
        string code,
        TimeZoneInfo zone,
        DateTimeOffset now,
        out DateTimeOffset start,
        out DateTimeOffset end)
    {
        start = default;
        end = default;

        if (date < OrganizationTime.LocalDate(now, zone))
        {
            return false;
        }

        if (code == Any)
        {
            start = OrganizationTime.StartOfDayUtc(date, zone);
            end = OrganizationTime.StartOfDayUtc(date.AddDays(1), zone);

            return true;
        }

        foreach (var window in Windows)
        {
            if (window.Code == code)
            {
                start = At(date, window.StartHour, zone);
                end = At(date, window.EndHour, zone);

                return true;
            }
        }

        return false;
    }

    /// <summary>The local date and window code of stored instants, in the zone of the branch.</summary>
    public static (DateOnly Date, string Code) Read(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var date = OrganizationTime.LocalDate(start, zone);

        foreach (var window in Windows)
        {
            if (At(date, window.StartHour, zone) == start && At(date, window.EndHour, zone) == end)
            {
                return (date, window.Code);
            }
        }

        return (date, Any);
    }

    // A local hour inside a DST gap uses the offset an hour later, like OrganizationTime.StartOfDayUtc.
    private static DateTimeOffset At(DateOnly date, int hour, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified);
        var offset = zone.IsInvalidTime(local) ? zone.GetUtcOffset(local.AddHours(1)) : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
