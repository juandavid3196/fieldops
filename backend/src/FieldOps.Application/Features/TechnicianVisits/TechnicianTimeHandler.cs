using System.Globalization;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.TechnicianVisits;

/// <summary>A time entry as read from persistence; instants are UTC and the visit fields feed the display view.</summary>
public sealed record TechnicianTimeEntryRow(
    Guid Id,
    VisitTimeEntryType Type,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    Guid VisitId,
    string DisplayNumber,
    string Title,
    string Status,
    int VisitNumber,
    bool VisitAccessible);

public sealed record TimeEntryVisit(Guid VisitId, string DisplayNumber, string Title, string Status, int VisitNumber);

/// <summary>technician-time-summary BR-05.</summary>
public sealed record TimeEntry(
    Guid Id,
    string Type,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int DurationSeconds,
    bool IsOpen,
    TimeEntryVisit Visit,
    bool VisitAccessible);

public sealed record TimeTotals(int WorkSeconds, int TravelSeconds, int BreakSeconds, int TotalSeconds);

public sealed record TimeDay(string Date, int WorkSeconds, int TravelSeconds, int BreakSeconds, int TotalSeconds);

/// <summary>GET /technician/time (technician-time-summary BR-01 to BR-05).</summary>
public sealed record TechnicianTime(
    string Timezone,
    string Today,
    string View,
    string From,
    string To,
    TimeTotals Totals,
    IReadOnlyList<TimeDay> Days,
    IReadOnlyList<TimeEntry> Entries);

/// <summary>Pure range, membership and duration rules of the time summary (BR-02 to BR-04).</summary>
public static class TechnicianTimeRules
{
    public const string DayView = "day";

    public const string WeekView = "week";

    private static readonly DateOnly FirstDate = new(2000, 1, 1);

    private static readonly DateOnly LastDate = new(2099, 12, 31);

    public static bool TryParseView(string? text, out string view)
    {
        view = text switch
        {
            null => WeekView,
            _ when string.Equals(text, DayView, StringComparison.OrdinalIgnoreCase) => DayView,
            _ when string.Equals(text, WeekView, StringComparison.OrdinalIgnoreCase) => WeekView,
            _ => string.Empty,
        };

        return view.Length > 0;
    }

    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
        && date >= FirstDate
        && date <= LastDate;

    /// <summary>The first and last local date of the range: the day itself, or the Monday to Sunday containing it.</summary>
    public static (DateOnly First, DateOnly Last) Range(string view, DateOnly date)
    {
        if (view == DayView)
        {
            return (date, date);
        }

        var monday = BranchTime.MondayOf(date);

        return (monday, monday.AddDays(6));
    }

    /// <summary>Whole seconds of a closed entry, or of an open one up to <paramref name="now"/>.</summary>
    public static int DurationSeconds(DateTimeOffset startedAt, DateTimeOffset? endedAt, DateTimeOffset now) =>
        VisitProgressRules.WholeSeconds(startedAt, endedAt ?? now);

    public static string TypeCode(VisitTimeEntryType type) => type switch
    {
        VisitTimeEntryType.Travel => "travel",
        VisitTimeEntryType.Pause => "break",
        _ => "work",
    };

    /// <summary>BR-03, BR-04: one row per local date; an entry counts wholly on the local day of its start.</summary>
    public static (IReadOnlyList<TimeDay> Days, TimeTotals Totals) Bucket(
        DateOnly first, DateOnly last, IEnumerable<(DateTimeOffset StartedAt, string Type, int Seconds)> entries, TimeZoneInfo zone)
    {
        var list = entries.Select(entry => (Date: BranchTime.LocalDate(entry.StartedAt, zone), entry.Type, entry.Seconds)).ToList();
        var days = new List<TimeDay>();

        for (var date = first; date <= last; date = date.AddDays(1))
        {
            var day = list.Where(entry => entry.Date == date).ToList();
            var (work, travel, rest) = Sum(day.Select(entry => (entry.Type, entry.Seconds)));

            days.Add(new TimeDay(Format(date), work, travel, rest, work + travel + rest));
        }

        var (totalWork, totalTravel, totalBreak) = Sum(list.Where(entry => entry.Date >= first && entry.Date <= last)
            .Select(entry => (entry.Type, entry.Seconds)));

        return (days, new TimeTotals(totalWork, totalTravel, totalBreak, totalWork + totalTravel + totalBreak));
    }

    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static (int Work, int Travel, int Break) Sum(IEnumerable<(string Type, int Seconds)> entries)
    {
        var rows = entries.ToList();

        return (
            rows.Where(entry => entry.Type == "work").Sum(entry => entry.Seconds),
            rows.Where(entry => entry.Type == "travel").Sum(entry => entry.Seconds),
            rows.Where(entry => entry.Type == "break").Sum(entry => entry.Seconds));
    }
}

/// <summary>GET /technician/time (technician-time-summary BR-01 to BR-05). Read-only; the profile and organization come from the session.</summary>
public sealed class GetTechnicianTimeHandler(ITechnicianVisitStore store, TimeProvider timeProvider)
{
    public const string DateInvalid = "Choose a valid date.";

    public async Task<TechnicianVisitResult<TechnicianTime>> HandleAsync(
        Guid organizationId, Guid membershipId, string? viewText, string? dateText, CancellationToken cancellationToken)
    {
        var (profile, failure) = await TechnicianVisitRules.ResolveAsync<TechnicianTime>(
            store, organizationId, membershipId, cancellationToken);

        if (profile is null)
        {
            return failure!;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!TechnicianTimeRules.TryParseView(viewText, out var view))
        {
            errors["view"] = [DateInvalid];
        }

        var now = timeProvider.GetUtcNow();
        var zone = BranchTime.FindZone(profile.ZoneId);
        var today = BranchTime.LocalDate(now, zone);
        var date = today;

        if (dateText is not null && !TechnicianTimeRules.TryParseDate(dateText, out date))
        {
            errors["date"] = [DateInvalid];
        }

        if (errors.Count > 0)
        {
            return TechnicianVisitResult<TechnicianTime>.Invalid(errors);
        }

        var (first, last) = TechnicianTimeRules.Range(view, date);
        var rows = await store.ListTimeEntriesAsync(
            organizationId,
            profile.Id,
            BranchTime.ToInstant(first, TimeOnly.MinValue, zone),
            BranchTime.ToInstant(last.AddDays(1), TimeOnly.MinValue, zone),
            cancellationToken);

        var entries = rows
            .OrderByDescending(row => row.StartedAt)
            .ThenByDescending(row => row.Id)
            .Select(row => new TimeEntry(
                row.Id,
                TechnicianTimeRules.TypeCode(row.Type),
                OrganizationTime.ToZone(row.StartedAt, zone),
                row.EndedAt is { } ended ? OrganizationTime.ToZone(ended, zone) : null,
                TechnicianTimeRules.DurationSeconds(row.StartedAt, row.EndedAt, now),
                row.EndedAt is null,
                new TimeEntryVisit(row.VisitId, row.DisplayNumber, row.Title, row.Status, row.VisitNumber),
                row.VisitAccessible))
            .ToList();
        var (days, totals) = TechnicianTimeRules.Bucket(
            first, last, entries.Select(entry => (entry.StartedAt, entry.Type, entry.DurationSeconds)), zone);

        return TechnicianVisitResult<TechnicianTime>.Ok(new TechnicianTime(
            profile.ZoneId,
            TechnicianTimeRules.Format(today),
            view,
            TechnicianTimeRules.Format(first),
            TechnicianTimeRules.Format(last),
            totals,
            days,
            entries));
    }
}
