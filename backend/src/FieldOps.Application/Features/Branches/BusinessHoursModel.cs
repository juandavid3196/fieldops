using System.Globalization;
using System.Text.Json;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Branches;

/// <summary>One weekday of the business-hours API body (business-hours API contracts).</summary>
public sealed record BusinessHoursDayView(string DayOfWeek, bool IsOpen, string? OpeningTime, string? ClosingTime);

/// <summary>Body of GET/PUT /branches/{branchId}/business-hours.</summary>
public sealed record BusinessHoursView(
    Guid BranchId,
    string BranchName,
    string? Timezone,
    DateTimeOffset UpdatedAt,
    bool CanManage,
    IReadOnlyList<BusinessHoursDayView> Days);

/// <summary>The non-blocking dispatch result of business-hours BR-12; never a conflict.</summary>
public sealed record BusinessHoursCheck(
    bool Outside, string DayOfWeek, bool IsOpen, string? OpeningTime, string? ClosingTime);

/// <summary>Raw PUT body: days stay a JSON element so structure violations map to one 400 key.</summary>
public sealed record UpdateBusinessHoursCommand(
    Guid OrganizationId,
    Guid BranchId,
    JsonElement? Days,
    string? UpdatedAt,
    Guid ActorUserId,
    System.Net.IPAddress? ClientIp);

public abstract record UpdateBusinessHoursResult
{
    private UpdateBusinessHoursResult()
    {
    }

    public sealed record Succeeded(BusinessHoursView View) : UpdateBusinessHoursResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : UpdateBusinessHoursResult;

    public sealed record NotFound : UpdateBusinessHoursResult;

    public sealed record Stale : UpdateBusinessHoursResult;
}

/// <summary>
/// Pure conversion, validation (BR-01 to BR-04) and BR-12 classification between the seven-day API shape and the
/// canonical <c>branches.business_hours</c> object (BR-05).
/// </summary>
public static class BusinessHoursRules
{
    public const string DaysKey = "days";

    public const string ClosingNotAfterOpeningMessage = "Closing time must be after opening time.";

    private static readonly string[] Properties = ["dayOfWeek", "isOpen", "openingTime", "closingTime"];

    /// <summary>The seven days, Monday to Sunday, of a stored object; absent or unreadable days are closed.</summary>
    public static IReadOnlyList<BusinessHoursDayView> FromStored(string storedJson)
    {
        JsonDocument? document = null;

        try
        {
            document = JsonDocument.Parse(storedJson);
            var root = document.RootElement;

            return [.. BusinessHoursValidator.Days.Select(day => ReadStoredDay(root, day))];
        }
        catch (JsonException)
        {
            return [.. BusinessHoursValidator.Days.Select(day => new BusinessHoursDayView(day, false, null, null))];
        }
        finally
        {
            document?.Dispose();
        }
    }

    /// <summary>
    /// Validates the request <c>days</c>. On success returns the seven days in Monday-Sunday order and the canonical
    /// stored JSON; on failure reports dotted keys through <paramref name="addError"/>.
    /// </summary>
    public static bool TryValidate(
        JsonElement? days,
        Action<string, string> addError,
        out IReadOnlyList<BusinessHoursDayView> normalized,
        out string canonicalJson)
    {
        normalized = [];
        canonicalJson = "{}";

        if (!TryReadStructure(days, out var parsed))
        {
            addError(DaysKey, BusinessHoursValidator.StructuralInvalidMessage);

            return false;
        }

        var isValid = true;
        var result = new List<BusinessHoursDayView>(7);
        var stored = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var day in BusinessHoursValidator.Days)
        {
            var item = parsed[day];
            var opening = item.Opening;
            var closing = item.Closing;

            if (!item.IsOpen)
            {
                if (opening.Present || closing.Present)
                {
                    addError($"{DaysKey}.{day}", BusinessHoursValidator.StructuralInvalidMessage);
                    isValid = false;
                }

                result.Add(new BusinessHoursDayView(day, false, null, null));

                continue;
            }

            var openingValid = ValidateTime(opening, $"{DaysKey}.{day}.openingTime", addError);
            var closingValid = ValidateTime(closing, $"{DaysKey}.{day}.closingTime", addError);

            if (openingValid && closingValid && string.CompareOrdinal(closing.Text, opening.Text) <= 0)
            {
                addError($"{DaysKey}.{day}.closingTime", ClosingNotAfterOpeningMessage);
                closingValid = false;
            }

            if (!openingValid || !closingValid)
            {
                isValid = false;

                continue;
            }

            result.Add(new BusinessHoursDayView(day, true, opening.Text, closing.Text));
            stored[day] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["start"] = opening.Text!,
                ["end"] = closing.Text!,
            };
        }

        if (!isValid)
        {
            return false;
        }

        // Reuses the canonical writer of organization-onboarding BR-15.
        var accepted = BusinessHoursValidator.TryValidate(
            JsonSerializer.SerializeToElement(stored), static (_, _) => { }, out canonicalJson);

        normalized = result;

        return accepted;
    }

    /// <summary>BR-12: null without a branch time zone; otherwise the day's hours against the local slot.</summary>
    public static BusinessHoursCheck? Check(string storedJson, string? timezone, DateTimeOffset start, DateTimeOffset end)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return null;
        }

        var zone = OrganizationTime.FindZone(timezone);
        var localStart = TimeZoneInfo.ConvertTime(start, zone);
        var localEnd = TimeZoneInfo.ConvertTime(end, zone);
        var dayIndex = ((int)localStart.DayOfWeek + 6) % 7;
        var day = FromStored(storedJson)[dayIndex];

        if (!day.IsOpen)
        {
            return new BusinessHoursCheck(true, day.DayOfWeek, false, null, null);
        }

        var startText = localStart.ToString("HH:mm", CultureInfo.InvariantCulture);
        var endText = localEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
        var outside = string.CompareOrdinal(startText, day.OpeningTime) < 0
            || string.CompareOrdinal(endText, day.ClosingTime) > 0;

        return new BusinessHoursCheck(outside, day.DayOfWeek, true, day.OpeningTime, day.ClosingTime);
    }

    /// <summary>Days whose entries differ, in Monday-Sunday order.</summary>
    public static IReadOnlyList<(BusinessHoursDayView Before, BusinessHoursDayView After)> ChangedDays(
        IReadOnlyList<BusinessHoursDayView> before, IReadOnlyList<BusinessHoursDayView> after) =>
        [.. before.Zip(after).Where(pair => pair.First != pair.Second)];

    private static BusinessHoursDayView ReadStoredDay(JsonElement root, string day)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(day, out var entry)
            && entry.ValueKind == JsonValueKind.Object
            && entry.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.String
            && entry.TryGetProperty("end", out var end) && end.ValueKind == JsonValueKind.String)
        {
            return new BusinessHoursDayView(day, true, start.GetString(), end.GetString());
        }

        return new BusinessHoursDayView(day, false, null, null);
    }

    private static bool ValidateTime(TimeField field, string key, Action<string, string> addError)
    {
        if (string.IsNullOrEmpty(field.Text) && !field.Malformed)
        {
            addError(key, BusinessHoursValidator.RequiredMessage);

            return false;
        }

        if (field.Malformed || !BusinessHoursValidator.IsValidTime(field.Text!))
        {
            addError(key, BusinessHoursValidator.StructuralInvalidMessage);

            return false;
        }

        return true;
    }

    // BR-01: exactly seven objects, one per lowercase weekday, only the four known properties.
    private static bool TryReadStructure(JsonElement? days, out Dictionary<string, ParsedDay> parsed)
    {
        parsed = new Dictionary<string, ParsedDay>(StringComparer.Ordinal);

        if (days is not { ValueKind: JsonValueKind.Array } array || array.GetArrayLength() != 7)
        {
            return false;
        }

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || element.EnumerateObject().Any(property => !Properties.Contains(property.Name)))
            {
                return false;
            }

            if (!element.TryGetProperty("dayOfWeek", out var dayElement)
                || dayElement.ValueKind != JsonValueKind.String
                || dayElement.GetString() is not { } day
                || Array.IndexOf(BusinessHoursValidator.Days, day) < 0
                || parsed.ContainsKey(day))
            {
                return false;
            }

            if (!element.TryGetProperty("isOpen", out var isOpen)
                || isOpen.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            parsed[day] = new ParsedDay(
                isOpen.GetBoolean(),
                ReadTime(element, "openingTime"),
                ReadTime(element, "closingTime"));
        }

        return parsed.Count == 7;
    }

    private static TimeField ReadTime(JsonElement day, string name)
    {
        if (!day.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return new TimeField(false, null, false);
        }

        return value.ValueKind == JsonValueKind.String
            ? new TimeField(true, value.GetString(), false)
            : new TimeField(true, null, true);
    }

    private readonly record struct TimeField(bool Present, string? Text, bool Malformed);

    private readonly record struct ParsedDay(bool IsOpen, TimeField Opening, TimeField Closing);
}
