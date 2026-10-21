using System.Globalization;
using FieldOps.Application.Validation;

namespace FieldOps.Application.Features.Team;

/// <summary>Weekday names of the API (Monday first) and their stored numbers (<c>day_of_week</c> 0 = Sunday).</summary>
public static class AvailabilityDays
{
    public static readonly string[] Names = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    public static int Number(string name) => (Array.IndexOf(Names, name) + 1) % 7;

    public static string Name(int dayOfWeek) => Names[(dayOfWeek + 6) % 7];
}

/// <summary>Validation of the availability, time off, grid, matrix and skill assignment requests.</summary>
public static class TeamAvailabilityRules
{
    public const int NoteMaxLength = 250;

    public const int MaxYear = 9000;

    public static DateTimeOffset? ParseUpdatedAt(string? text) => UpdatedAtValidation.TryParse(text, out var parsed) ? parsed : null;

    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date.Year < MaxYear;

    /// <summary>BR-09: exactly seven distinct days, valid times, end after start. Null with the keyed errors when invalid.</summary>
    public static IReadOnlyList<AvailabilityDayValues>? ValidateDays(
        IReadOnlyList<AvailabilityDayInput>? days, Dictionary<string, string[]> errors)
    {
        var before = errors.Count;

        if (days is null
            || days.Count != 7
            || days.Any(day => day is null || day.DayOfWeek is null || !AvailabilityDays.Names.Contains(day.DayOfWeek))
            || days.Select(day => day.DayOfWeek).Distinct(StringComparer.Ordinal).Count() != 7)
        {
            errors[TeamAvailabilityKeys.Days] = [TeamAvailabilityMessages.DaysInvalid];

            return null;
        }

        var result = new List<AvailabilityDayValues>(7);

        foreach (var day in days.OrderBy(item => Array.IndexOf(AvailabilityDays.Names, item.DayOfWeek)))
        {
            var key = $"{TeamAvailabilityKeys.Days}.{day.DayOfWeek}";
            var number = AvailabilityDays.Number(day.DayOfWeek!);

            if (day.IsAvailable is null)
            {
                errors[$"{key}.isAvailable"] = [TeamAvailabilityMessages.Required];

                continue;
            }

            if (day.IsAvailable == false)
            {
                if (day.Start is not null || day.End is not null)
                {
                    errors[key] = [TeamAvailabilityMessages.DaysInvalid];

                    continue;
                }

                result.Add(new AvailabilityDayValues(number, false, null, null));

                continue;
            }

            var startOk = TryTime(day.Start, $"{key}.start", errors, out var start);
            var endOk = TryTime(day.End, $"{key}.end", errors, out var end);

            if (startOk && endOk && end <= start)
            {
                errors[$"{key}.end"] = [TeamAvailabilityMessages.EndAfterStart];

                continue;
            }

            if (startOk && endOk)
            {
                result.Add(new AvailabilityDayValues(number, true, start, end));
            }
        }

        return errors.Count > before ? null : result;
    }

    /// <summary>Time off rules (BR-14, time off validation table). <paramref name="today"/> is the technician's local date.</summary>
    public static TimeOffValues? ValidateTimeOff(CreateTimeOffInput input, DateOnly today, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var startOk = TryDate(input.StartDate, TeamAvailabilityKeys.StartDate, errors, out var start);
        var endOk = TryDate(input.EndDate, TeamAvailabilityKeys.EndDate, errors, out var end);

        if (startOk && start < today)
        {
            errors[TeamAvailabilityKeys.StartDate] = [TeamAvailabilityMessages.StartDateInvalid];
        }

        if (startOk && endOk && end < start)
        {
            errors[TeamAvailabilityKeys.EndDate] = [TeamAvailabilityMessages.EndDateBeforeStart];
        }

        var note = input.Note?.Trim();

        if (note is { Length: > NoteMaxLength })
        {
            errors[TeamAvailabilityKeys.Note] = [TeamAvailabilityMessages.NoteTooLong];
        }

        return errors.Count > 0 ? null : new TimeOffValues(start, end, string.IsNullOrEmpty(note) ? null : note);
    }

    /// <summary>BR-21: the submitted ids when each is a guid and appears once; the key error otherwise.</summary>
    public static IReadOnlyList<Guid>? ValidateSkillIds(IReadOnlyList<string>? ids, Dictionary<string, string[]> errors)
    {
        if (ids is null)
        {
            errors[TeamAvailabilityKeys.SkillIds] = [TeamAvailabilityMessages.Required];

            return null;
        }

        var parsed = new List<Guid>(ids.Count);

        foreach (var text in ids)
        {
            if (!Guid.TryParse(text, out var id) || id == Guid.Empty)
            {
                errors[TeamAvailabilityKeys.SkillIds] = [TeamAvailabilityMessages.SkillNotActive];

                return null;
            }

            parsed.Add(id);
        }

        if (parsed.Distinct().Count() != parsed.Count)
        {
            errors[TeamAvailabilityKeys.SkillIds] = [TeamAvailabilityMessages.SkillDuplicated];

            return null;
        }

        return parsed;
    }

    /// <summary>The grid query (BR-02, BR-06): the Monday <paramref name="weekStart"/>, filters and page, or null with the errors.</summary>
    public static (DateOnly WeekStart, string? Search, Guid? BranchId, string Availability, int Page)? ParseGrid(
        TeamAvailabilityQuery query, DateOnly defaultWeekStart, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var weekStart = defaultWeekStart;

        if (!string.IsNullOrWhiteSpace(query.WeekStart)
            && (!TryParseDate(query.WeekStart, out weekStart) || weekStart.DayOfWeek != DayOfWeek.Monday))
        {
            errors[TeamAvailabilityKeys.WeekStart] = [TeamMessages.QueryInvalid];
        }

        var search = ParseSearch(query.Search, errors);
        var branchId = ParseBranch(query.BranchId, errors);
        var availability = "all";

        if (!string.IsNullOrEmpty(query.Availability))
        {
            if (query.Availability is "all" or "available" or "time_off" or "none")
            {
                availability = query.Availability;
            }
            else
            {
                errors[TeamAvailabilityKeys.Availability] = [TeamMessages.QueryInvalid];
            }
        }

        var page = ParsePage(query.Page, errors);

        return errors.Count > 0 ? null : (weekStart, search, branchId, availability, page);
    }

    /// <summary>The matrix query (BR-17, BR-18), or null with the errors.</summary>
    public static (string? Search, Guid? BranchId, Guid? SkillId, string Coverage, bool Descending, int Page)? ParseMatrix(
        SkillsMatrixQuery query, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var search = ParseSearch(query.Search, errors);
        var branchId = ParseBranch(query.BranchId, errors);
        Guid? skillId = null;

        if (!string.IsNullOrEmpty(query.SkillId))
        {
            if (TeamQueryParser.TryParseGuid(query.SkillId, out var parsed))
            {
                skillId = parsed;
            }
            else
            {
                errors[TeamFieldKeys.SkillId] = [TeamMessages.SkillInvalid];
            }
        }

        var coverage = "all";

        if (!string.IsNullOrEmpty(query.Coverage))
        {
            if (query.Coverage is "all" or "with" or "without")
            {
                coverage = query.Coverage;
            }
            else
            {
                errors[TeamAvailabilityKeys.Coverage] = [TeamMessages.QueryInvalid];
            }
        }

        var descending = false;

        if (!string.IsNullOrEmpty(query.Sort))
        {
            if (query.Sort is "asc" or "desc")
            {
                descending = query.Sort == "desc";
            }
            else
            {
                errors[TeamFieldKeys.Sort] = [TeamMessages.QueryInvalid];
            }
        }

        var page = ParsePage(query.Page, errors);

        return errors.Count > 0 ? null : (search, branchId, skillId, coverage, descending, page);
    }

    private static string? ParseSearch(string? text, Dictionary<string, string[]> errors)
    {
        var search = text?.Trim();

        if (search is { Length: > TeamQueryParser.SearchMaxLength })
        {
            errors[TeamFieldKeys.Search] = [TeamMessages.SearchTooLong];
        }

        return string.IsNullOrEmpty(search) ? null : search;
    }

    private static Guid? ParseBranch(string? text, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (TeamQueryParser.TryParseGuid(text, out var id))
        {
            return id;
        }

        errors[TeamFieldKeys.BranchId] = [TeamMessages.BranchNotAllowed];

        return null;
    }

    private static int ParsePage(string? text, Dictionary<string, string[]> errors)
    {
        var page = 1;

        if (!string.IsNullOrEmpty(text)
            && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors[TeamFieldKeys.Page] = [TeamMessages.QueryInvalid];
        }

        return page;
    }

    private static bool TryTime(string? text, string key, Dictionary<string, string[]> errors, out TimeOnly time)
    {
        time = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            errors[key] = [TeamAvailabilityMessages.Required];

            return false;
        }

        if (!TimeOnly.TryParseExact(text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        {
            errors[key] = [TeamAvailabilityMessages.DaysInvalid];

            return false;
        }

        return true;
    }

    private static bool TryDate(string? text, string key, Dictionary<string, string[]> errors, out DateOnly date)
    {
        date = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            errors[key] = [TeamAvailabilityMessages.Required];

            return false;
        }

        if (!TryParseDate(text, out date))
        {
            errors[key] = [TeamMessages.QueryInvalid];

            return false;
        }

        return true;
    }
}
