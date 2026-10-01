using System.Globalization;
using FieldOps.Application.Validation;

namespace FieldOps.Application.Features.Team;

/// <summary>Validation of the skills and availability page inputs (BR-05, BR-06, BR-09 to BR-12, BR-17). Pure.</summary>
public static class SkillsAvailabilityRules
{
    public const int ReasonMaxLength = 200;

    public const int SkillNameMaxLength = 120;

    public const int SkillDescriptionMaxLength = 500;

    public const string VersionKey = "version";

    public const string WeeklyKey = "weeklyAvailability";

    public const string SkillsKey = "skills";

    /// <summary>The parsed <c>version</c> token (an <c>updated_at</c> instant), or null when missing or malformed.</summary>
    public static DateTimeOffset? ParseVersion(string? text) =>
        UpdatedAtValidation.TryParse(text, out var parsed) ? parsed : null;

    /// <summary>Formats the opaque version token of an <c>updated_at</c> value (round-trip, UTC).</summary>
    public static string FormatVersion(DateTimeOffset updatedAt) =>
        updatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Truncates to the microsecond precision PostgreSQL stores, so tokens round-trip exactly.</summary>
    public static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);

    public static bool TryParseTime(string? text, out TimeOnly time)
    {
        time = default;

        return !string.IsNullOrWhiteSpace(text)
            && TimeOnly.TryParseExact(text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
            && time.Minute % 30 == 0;
    }

    public static SkillsAvailabilityValues? ValidateSave(
        SaveSkillsAvailabilityInput input, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (ParseVersion(input.Version) is null)
        {
            Add(errors, VersionKey, UpdatedAtValidation.InvalidMessage);
        }

        var weekly = ValidateWeekly(input.WeeklyAvailability, errors);
        var skills = ValidateSkills(input.Skills, errors);

        return errors.Count > 0 ? null : new SkillsAvailabilityValues(weekly, skills);
    }

    private static List<WeeklyDayValues> ValidateWeekly(
        IReadOnlyList<WeeklyDayInput>? days, Dictionary<string, string[]> errors)
    {
        var result = new List<WeeklyDayValues>();

        if (days is null)
        {
            Add(errors, WeeklyKey, TeamMessages.QueryInvalid);

            return result;
        }

        var seen = new HashSet<int>();

        foreach (var day in days)
        {
            if (day?.DayOfWeek is not (>= 0 and <= 6) || day.DayOfWeek is not { } dayOfWeek)
            {
                Add(errors, WeeklyKey, TeamMessages.QueryInvalid);

                continue;
            }

            if (!seen.Add(dayOfWeek))
            {
                Add(errors, WeeklyKey, SkillsAvailabilityMessages.DayDuplicated);

                continue;
            }

            var prefix = $"{WeeklyKey}[{dayOfWeek}]";
            var startOk = TryParseTime(day.Start, out var start);
            var endOk = TryParseTime(day.End, out var end);

            if (!startOk)
            {
                Add(errors, $"{prefix}.start", SkillsAvailabilityMessages.TimesRequired);
            }

            if (!endOk)
            {
                Add(errors, $"{prefix}.end", SkillsAvailabilityMessages.TimesRequired);
            }

            var windowOk = startOk && endOk;

            if (windowOk && end <= start)
            {
                Add(errors, $"{prefix}.end", SkillsAvailabilityMessages.EndAfterStart);
                windowOk = false;
            }

            TimeRange? range = null;
            var hasBreak = !string.IsNullOrWhiteSpace(day.BreakStart) || !string.IsNullOrWhiteSpace(day.BreakEnd);

            if (hasBreak)
            {
                var breakOk = TryParseTime(day.BreakStart, out var breakStart)
                    & TryParseTime(day.BreakEnd, out var breakEnd)
                    && (breakEnd - breakStart).TotalMinutes is 30 or 60
                    && (!windowOk || (breakStart > start && breakEnd < end));

                if (breakOk)
                {
                    range = new TimeRange(breakStart, breakEnd);
                }
                else
                {
                    Add(errors, $"{prefix}.breakStart", SkillsAvailabilityMessages.BreakMustFit);
                }
            }

            if (windowOk)
            {
                result.Add(new WeeklyDayValues(dayOfWeek, start, end, range));
            }
        }

        return result;
    }

    private static List<SkillAssignmentValues> ValidateSkills(
        IReadOnlyList<SkillAssignmentInput>? skills, Dictionary<string, string[]> errors)
    {
        var result = new List<SkillAssignmentValues>();

        if (skills is null)
        {
            Add(errors, SkillsKey, TeamMessages.QueryInvalid);

            return result;
        }

        var seen = new HashSet<Guid>();
        var failed = false;

        foreach (var skill in skills)
        {
            if (skill is null || !TeamQueryParser.TryParseGuid(skill.SkillId, out var skillId))
            {
                Add(errors, SkillsKey, TeamMessages.SkillInvalid);
                failed = true;

                continue;
            }

            if (!seen.Add(skillId))
            {
                Add(errors, SkillsKey, SkillsAvailabilityMessages.SkillDuplicated);
                failed = true;

                continue;
            }

            if (skill.Proficiency is not (>= 1 and <= 5) || skill.Proficiency is not { } level)
            {
                Add(errors, SkillsKey, SkillsAvailabilityMessages.LevelRequired);
                failed = true;

                continue;
            }

            result.Add(new SkillAssignmentValues(skillId, (short)level, skill.IsPrimary == true));
        }

        if (!failed && result.Count > 0 && result.Count(skill => skill.IsPrimary) != 1)
        {
            Add(errors, SkillsKey, SkillsAvailabilityMessages.PrimaryRequired);
        }

        return result;
    }

    /// <summary>Exception form rules (BR-12, "Exception validation"); <paramref name="today"/> is the local date (BR-03).</summary>
    public static ExceptionValues? ValidateException(
        ExceptionInput input, DateOnly today, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        DateOnly date = default;

        if (string.IsNullOrWhiteSpace(input.Date)
            || !DateOnly.TryParseExact(input.Date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || date < today)
        {
            Add(errors, "date", SkillsAvailabilityMessages.DateInvalid);
        }

        var kind = input.Kind?.Trim();

        if (kind is not (ExceptionKinds.Extended or ExceptionKinds.Partial or ExceptionKinds.Unavailable))
        {
            Add(errors, "kind", SkillsAvailabilityMessages.KindInvalid);
            kind = null;
        }

        TimeOnly? start = null;
        TimeOnly? end = null;

        if (kind == ExceptionKinds.Unavailable)
        {
            if (!string.IsNullOrWhiteSpace(input.Start) || !string.IsNullOrWhiteSpace(input.End))
            {
                Add(errors, "start", SkillsAvailabilityMessages.TimesNotAllowed);
            }
        }
        else if (kind is not null)
        {
            var startOk = TryParseTime(input.Start, out var parsedStart);
            var endOk = TryParseTime(input.End, out var parsedEnd);

            if (!startOk)
            {
                Add(errors, "start", SkillsAvailabilityMessages.TimesRequired);
            }

            if (!endOk)
            {
                Add(errors, "end", SkillsAvailabilityMessages.TimesRequired);
            }

            if (startOk && endOk && parsedEnd <= parsedStart)
            {
                Add(errors, "end", SkillsAvailabilityMessages.EndAfterStart);
            }
            else if (startOk && endOk)
            {
                start = parsedStart;
                end = parsedEnd;
            }
        }

        var reason = input.Reason?.Trim() ?? string.Empty;

        if (reason.Length == 0)
        {
            Add(errors, "reason", SkillsAvailabilityMessages.ReasonRequired);
        }
        else if (reason.Length > ReasonMaxLength)
        {
            Add(errors, "reason", SkillsAvailabilityMessages.ReasonTooLong);
        }

        return errors.Count > 0 || kind is null ? null : new ExceptionValues(date, kind, start, end, reason);
    }

    public static SkillValues? ValidateSkill(SkillInput input, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var name = input.Name?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();

        if (name.Length == 0)
        {
            Add(errors, "name", SkillsAvailabilityMessages.SkillNameRequired);
        }
        else if (name.Length > SkillNameMaxLength)
        {
            Add(errors, "name", SkillsAvailabilityMessages.SkillNameTooLong);
        }

        if (description is { Length: > SkillDescriptionMaxLength })
        {
            Add(errors, "description", SkillsAvailabilityMessages.SkillDescriptionTooLong);
        }

        return errors.Count > 0 ? null : new SkillValues(name, description);
    }

    private static void Add(Dictionary<string, string[]> errors, string key, string message) =>
        errors[key] = errors.TryGetValue(key, out var existing) ? [.. existing, message] : [message];
}
