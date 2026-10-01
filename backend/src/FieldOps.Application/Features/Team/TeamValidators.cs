using System.Text.RegularExpressions;
using FieldOps.Application.Features.Customers;

namespace FieldOps.Application.Features.Team;

/// <summary>Field validation of the create/edit form (BR-15, BR-16). Pure.</summary>
public static partial class TeamRules
{
    public const int NameMaxLength = 100;

    public const int EmailMaxLength = 254;

    public const int PhoneMaxLength = 40;

    public const int EmployeeCodeMaxLength = 50;

    public const int NotesMaxLength = 2000;

    public static TeamProfileValues? Validate(TeamProfileInput input, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var firstName = input.FirstName?.Trim() ?? string.Empty;
        var lastName = input.LastName?.Trim() ?? string.Empty;

        if (firstName.Length == 0)
        {
            errors[TeamFieldKeys.FirstName] = [TeamMessages.FirstNameRequired];
        }
        else if (firstName.Length > NameMaxLength)
        {
            errors[TeamFieldKeys.FirstName] = [TeamMessages.NameTooLong];
        }

        if (lastName.Length == 0)
        {
            errors[TeamFieldKeys.LastName] = [TeamMessages.LastNameRequired];
        }
        else if (lastName.Length > NameMaxLength)
        {
            errors[TeamFieldKeys.LastName] = [TeamMessages.NameTooLong];
        }

        var email = CustomerNormalizer.NormalizeEmail(input.Email);

        if (email is not null && (email.Length > EmailMaxLength || !CustomerNormalizer.IsValidEmail(email)))
        {
            errors[TeamFieldKeys.Email] = [TeamMessages.EmailInvalid];
        }

        var phone = NullIfBlank(input.Phone);

        if (phone is not null && !IsValidPhone(phone))
        {
            errors[TeamFieldKeys.Phone] = [TeamMessages.PhoneInvalid];
        }

        var code = NullIfBlank(input.EmployeeCode);

        if (code is not null && (code.Length > EmployeeCodeMaxLength || !EmployeeCodePattern().IsMatch(code)))
        {
            errors[TeamFieldKeys.EmployeeCode] = [TeamMessages.EmployeeCodeInvalid];
        }

        var notes = NullIfBlank(input.Notes);

        if (notes is not null && notes.Length > NotesMaxLength)
        {
            errors[TeamFieldKeys.Notes] = [TeamMessages.NotesTooLong];
        }

        return errors.Count > 0
            ? null
            : new TeamProfileValues(firstName, lastName, email, phone, code?.ToUpperInvariant(), notes);
    }

    /// <summary>Digits, spaces and <c>+ ( ) - .</c> only, 7 to 15 digits, at most 40 characters.</summary>
    public static bool IsValidPhone(string phone)
    {
        if (phone.Length > PhoneMaxLength)
        {
            return false;
        }

        var digits = 0;

        foreach (var c in phone)
        {
            if (char.IsAsciiDigit(c))
            {
                digits++;
            }
            else if (c is not (' ' or '+' or '(' or ')' or '-' or '.'))
            {
                return false;
            }
        }

        return digits is >= 7 and <= 15;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmployeeCodePattern();
}

/// <summary>Query parsing of the list, metrics and coverage endpoints (BR-09 to BR-11).</summary>
public static class TeamQueryParser
{
    public const int PageSize = 10;

    public const int SearchMaxLength = 100;

    public static bool TryParseGuid(string? text, out Guid id) =>
        Guid.TryParse(text, out id) && id != Guid.Empty;

    public static bool TryParsePeriod(string? text, out TeamPeriod period)
    {
        period = TeamPeriod.Today;

        switch (text)
        {
            case null or "" or "today":
                return true;
            case "week":
                period = TeamPeriod.Week;

                return true;
            default:
                return false;
        }
    }

    /// <summary>The validated filter (branch and skill ownership are checked by the handler), or null with the per-key errors.</summary>
    public static TeamListFilter? Parse(TeamListQuery query, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var search = query.Search?.Trim();

        if (search is { Length: > SearchMaxLength })
        {
            errors[TeamFieldKeys.Search] = [TeamMessages.SearchTooLong];
        }

        Guid? branchId = null;

        if (!string.IsNullOrEmpty(query.BranchId))
        {
            if (TryParseGuid(query.BranchId, out var parsed))
            {
                branchId = parsed;
            }
            else
            {
                errors[TeamFieldKeys.BranchId] = [TeamMessages.BranchNotAllowed];
            }
        }

        Guid? skillId = null;

        if (!string.IsNullOrEmpty(query.SkillId))
        {
            if (TryParseGuid(query.SkillId, out var parsed))
            {
                skillId = parsed;
            }
            else
            {
                errors[TeamFieldKeys.SkillId] = [TeamMessages.SkillInvalid];
            }
        }

        var status = TeamStatusFilter.AllActive;

        if (!string.IsNullOrEmpty(query.Status))
        {
            switch (query.Status)
            {
                case "all_active": status = TeamStatusFilter.AllActive; break;
                case "available": status = TeamStatusFilter.Available; break;
                case "on_job": status = TeamStatusFilter.OnJob; break;
                case "break": status = TeamStatusFilter.Break; break;
                case "time_off": status = TeamStatusFilter.TimeOff; break;
                case "off": status = TeamStatusFilter.Off; break;
                case "inactive": status = TeamStatusFilter.Inactive; break;
                case "suspended": status = TeamStatusFilter.Suspended; break;
                default: errors[TeamFieldKeys.Status] = [TeamMessages.QueryInvalid]; break;
            }
        }

        var link = TeamAccountFilter.All;

        if (!string.IsNullOrEmpty(query.AccountLink))
        {
            switch (query.AccountLink)
            {
                case "all": link = TeamAccountFilter.All; break;
                case "linked": link = TeamAccountFilter.Linked; break;
                case "not_linked": link = TeamAccountFilter.NotLinked; break;
                default: errors[TeamFieldKeys.AccountLink] = [TeamMessages.QueryInvalid]; break;
            }
        }

        var descending = false;

        if (!string.IsNullOrEmpty(query.Sort))
        {
            switch (query.Sort)
            {
                case "name_asc": descending = false; break;
                case "name_desc": descending = true; break;
                default: errors[TeamFieldKeys.Sort] = [TeamMessages.QueryInvalid]; break;
            }
        }

        if (!TryParsePeriod(query.Period, out var period))
        {
            errors[TeamFieldKeys.Period] = [TeamMessages.QueryInvalid];
        }

        var page = 1;

        if (!string.IsNullOrEmpty(query.Page)
            && (!int.TryParse(query.Page, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out page)
                || page < 1))
        {
            errors[TeamFieldKeys.Page] = [TeamMessages.QueryInvalid];
        }

        return errors.Count > 0
            ? null
            : new TeamListFilter(string.IsNullOrEmpty(search) ? null : search, branchId, skillId, status, link, descending, period, page);
    }
}

/// <summary>BR-09 search: name, email and profile ID contain the term; a term with 3+ digits also matches the phone digits.</summary>
public static class TeamSearch
{
    public static bool Matches(
        string? term, string firstName, string lastName, string? email, string? employeeCode, string? phone)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return true;
        }

        var needle = term.Trim();

        bool Contains(string? text) => text is not null && text.Contains(needle, StringComparison.OrdinalIgnoreCase);

        if (Contains(firstName) || Contains(lastName) || Contains($"{firstName} {lastName}")
            || Contains(email) || Contains(employeeCode))
        {
            return true;
        }

        var digits = CustomerNormalizer.DigitsOnly(needle);

        return digits.Length >= 3 && phone is not null && CustomerNormalizer.DigitsOnly(phone).Contains(digits, StringComparison.Ordinal);
    }
}
