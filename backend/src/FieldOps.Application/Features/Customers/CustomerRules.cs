using System.Text.RegularExpressions;
using FieldOps.Domain.Customers;

namespace FieldOps.Application.Features.Customers;

/// <summary>Unvalidated customer fields (BR-10); the same shape feeds the form and each CSV row.</summary>
public sealed record CustomerInput(
    string? Type,
    string? CompanyName,
    string? FirstName,
    string? LastName,
    string? Title,
    string? Email,
    string? Phone,
    bool PrefersEmail,
    bool PrefersSms,
    string? AddressLine1,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? ServiceInstructions,
    string? InternalNote);

/// <summary>A create or edit body: the fields plus the raw branch and tag ids (verified by the handler).</summary>
public sealed record CustomerWriteInput(CustomerInput Fields, string? BranchId, IReadOnlyList<string>? TagIds);

/// <summary>Field validation shared by the JSON body and the CSV import (BR-10 to BR-13).</summary>
public static partial class CustomerRules
{
    public const string TypeKey = "type";

    public const string CompanyNameKey = "companyName";

    public const string FirstNameKey = "firstName";

    public const string LastNameKey = "lastName";

    public const string TitleKey = "title";

    public const string EmailKey = "email";

    public const string PhoneKey = "phone";

    public const string PreferredCommunicationKey = "preferredCommunication";

    public const string AddressLine1Key = "addressLine1";

    public const string CityKey = "city";

    public const string StateRegionKey = "stateRegion";

    public const string PostalCodeKey = "postalCode";

    public const string ServiceInstructionsKey = "serviceInstructions";

    public const string InternalNoteKey = "internalNote";

    public const string BranchIdKey = "branchId";

    public const string TagIdsKey = "tagIds";

    public const int MaxTags = 10;

    private const int LongTextMaxLength = 2000;

    private static readonly HashSet<string> UsStates = new(StringComparer.Ordinal)
    {
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL", "GA", "HI", "ID", "IL", "IN", "IA", "KS",
        "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ", "NM", "NY", "NC",
        "ND", "OH", "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY",
    };

    public static bool IsUnitedStates(string? countryCode) =>
        string.IsNullOrWhiteSpace(countryCode) || string.Equals(countryCode.Trim(), "US", StringComparison.OrdinalIgnoreCase);

    public static bool TryParseType(string? text, out CustomerType type)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "residential":
                type = CustomerType.Person;
                return true;
            case "commercial":
                type = CustomerType.Company;
                return true;
            default:
                type = default;
                return false;
        }
    }

    /// <summary>
    /// Validates every field and returns the values, or null with one message per key. When
    /// <paramref name="fixedType"/> is given the input type is ignored; create and edit pass null (the type is required and may change).
    /// </summary>
    public static CustomerValues? Validate(
        CustomerInput input,
        CustomerType? fixedType,
        bool unitedStates,
        out IReadOnlyDictionary<string, string> errors)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var type = fixedType ?? default;

        if (fixedType is null && !TryParseType(input.Type, out type))
        {
            found[TypeKey] = CustomerMessages.TypeRequired;
        }

        var commercial = fixedType is not null ? type == CustomerType.Company : TryParseType(input.Type, out var parsed) && parsed == CustomerType.Company;

        var companyName = input.CompanyName?.Trim() ?? string.Empty;
        var firstName = input.FirstName?.Trim() ?? string.Empty;
        var lastName = input.LastName?.Trim() ?? string.Empty;
        var title = Blank(input.Title);

        if (commercial)
        {
            if (companyName.Length == 0)
            {
                found[CompanyNameKey] = CustomerMessages.CompanyRequired;
            }
            else if (companyName.Length > 180)
            {
                found[CompanyNameKey] = CustomerMessages.CompanyTooLong;
            }

            if (title is { Length: > 100 })
            {
                found[TitleKey] = CustomerMessages.TitleTooLong;
            }
        }
        else
        {
            title = null;
        }

        if (firstName.Length == 0)
        {
            found[FirstNameKey] = CustomerMessages.FirstNameRequired;
        }
        else if (firstName.Length > 100)
        {
            found[FirstNameKey] = CustomerMessages.FirstNameTooLong;
        }

        if (lastName.Length == 0)
        {
            found[LastNameKey] = CustomerMessages.LastNameRequired;
        }
        else if (lastName.Length > 100)
        {
            found[LastNameKey] = CustomerMessages.LastNameTooLong;
        }
        else if (!commercial && firstName.Length <= 100 && firstName.Length + 1 + lastName.Length > 180)
        {
            found[LastNameKey] = CustomerMessages.FullNameTooLong;
        }

        var email = CustomerNormalizer.NormalizeEmail(input.Email);

        if (email is null)
        {
            found[EmailKey] = CustomerMessages.EmailRequired;
        }
        else if (!CustomerNormalizer.IsValidEmail(email))
        {
            found[EmailKey] = CustomerMessages.EmailInvalid;
        }

        string? phone = null;

        if (!CustomerNormalizer.TryNormalizePhone(input.Phone, out phone))
        {
            found[PhoneKey] = CustomerMessages.PhoneInvalid;
        }

        if (!input.PrefersEmail && !input.PrefersSms)
        {
            found[PreferredCommunicationKey] = CustomerMessages.ChannelRequired;
        }
        else if (input.PrefersSms && phone is null && !found.ContainsKey(PhoneKey))
        {
            found[PreferredCommunicationKey] = CustomerMessages.SmsNeedsPhone;
        }

        var address = input.AddressLine1?.Trim() ?? string.Empty;

        if (address.Length == 0)
        {
            found[AddressLine1Key] = CustomerMessages.AddressRequired;
        }
        else if (address.Length > 180)
        {
            found[AddressLine1Key] = CustomerMessages.AddressTooLong;
        }

        var city = input.City?.Trim() ?? string.Empty;

        if (city.Length == 0)
        {
            found[CityKey] = CustomerMessages.CityRequired;
        }
        else if (city.Length > 100)
        {
            found[CityKey] = CustomerMessages.CityTooLong;
        }

        var state = NormalizeState(input.StateRegion, unitedStates, out var stateError);

        if (stateError is not null)
        {
            found[StateRegionKey] = stateError;
        }

        var postal = Blank(input.PostalCode);

        if (!IsValidPostal(postal, unitedStates))
        {
            found[PostalCodeKey] = CustomerMessages.PostalInvalid;
        }

        var instructions = Blank(input.ServiceInstructions);

        if (instructions is { Length: > LongTextMaxLength })
        {
            found[ServiceInstructionsKey] = CustomerMessages.TextTooLong;
        }

        var note = Blank(input.InternalNote);

        if (note is { Length: > LongTextMaxLength })
        {
            found[InternalNoteKey] = CustomerMessages.TextTooLong;
        }

        errors = found;

        if (found.Count > 0)
        {
            return null;
        }

        return new CustomerValues(
            type,
            commercial ? companyName : $"{firstName} {lastName}",
            commercial ? companyName : null,
            firstName,
            lastName,
            title,
            email!,
            phone,
            input.PrefersEmail,
            input.PrefersSms,
            address,
            city,
            state,
            postal,
            instructions,
            note);
    }

    /// <summary>The trimmed state (upper-cased for the United States) and the BR-10 error, if any.</summary>
    internal static string? NormalizeState(string? raw, bool unitedStates, out string? error)
    {
        error = null;
        var state = Blank(raw);

        if (state is null)
        {
            return null;
        }

        if (unitedStates)
        {
            state = state.ToUpperInvariant();

            if (!UsStates.Contains(state))
            {
                error = CustomerMessages.StateInvalid;
            }
        }
        else if (state.Length > 100)
        {
            error = CustomerMessages.StateTooLong;
        }

        return state;
    }

    internal static bool IsValidPostal(string? postal, bool unitedStates) =>
        postal is null || (unitedStates ? ZipPattern().IsMatch(postal) : postal.Length <= 30);

    internal static string? Blank(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    [GeneratedRegex(@"^\d{5}(-\d{4})?$", RegexOptions.CultureInvariant)]
    private static partial Regex ZipPattern();
}

/// <summary>Raw BR-06 to BR-08 list query values (text, so bad input becomes a 400 key).</summary>
public sealed record CustomerListQuery(
    string? Tab,
    string? Search,
    string? Type,
    string? BranchId,
    IReadOnlyList<string>? TagIds,
    string? BalanceStatus,
    string? Sort,
    string? Page);

/// <summary>Validation of the list and metrics queries.</summary>
public static class CustomerQueryParser
{
    public const int PageSize = 10;

    public const int SearchMaxLength = 100;

    public static bool TryParseGuid(string? text, out Guid id) =>
        Guid.TryParse(text, out id) && id != Guid.Empty;

    /// <summary>The validated filter (branch scope is checked by the handler), or null with the per-key errors.</summary>
    public static CustomerListFilter? Parse(CustomerListQuery query, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var tab = CustomerTab.All;

        if (!string.IsNullOrEmpty(query.Tab))
        {
            switch (query.Tab)
            {
                case "all":
                    break;
                case "leads":
                    tab = CustomerTab.Leads;
                    break;
                case "active":
                    tab = CustomerTab.Active;
                    break;
                case "archived":
                    tab = CustomerTab.Archived;
                    break;
                default:
                    errors["tab"] = ["Choose all, leads, active or archived."];
                    break;
            }
        }

        var search = query.Search?.Trim();

        if (search is { Length: > SearchMaxLength })
        {
            errors["search"] = ["Use 100 characters or fewer."];
        }

        CustomerType? type = null;

        if (!string.IsNullOrEmpty(query.Type))
        {
            if (CustomerRules.TryParseType(query.Type, out var parsedType) && query.Type == query.Type.ToLowerInvariant())
            {
                type = parsedType;
            }
            else
            {
                errors["type"] = ["Choose residential or commercial."];
            }
        }

        Guid? branchId = null;

        if (!string.IsNullOrEmpty(query.BranchId))
        {
            if (TryParseGuid(query.BranchId, out var parsedBranch))
            {
                branchId = parsedBranch;
            }
            else
            {
                errors[CustomerRules.BranchIdKey] = [CustomerMessages.BranchNotAllowed];
            }
        }

        var tagIds = new List<Guid>();

        foreach (var text in query.TagIds ?? [])
        {
            if (TryParseGuid(text, out var tagId))
            {
                tagIds.Add(tagId);
            }
            else
            {
                errors[CustomerRules.TagIdsKey] = [CustomerMessages.TagInvalid];
            }
        }

        var balance = CustomerBalanceFilter.Any;

        if (!string.IsNullOrEmpty(query.BalanceStatus))
        {
            switch (query.BalanceStatus)
            {
                case "any":
                    break;
                case "none":
                    balance = CustomerBalanceFilter.None;
                    break;
                case "has_balance":
                    balance = CustomerBalanceFilter.HasBalance;
                    break;
                case "overdue":
                    balance = CustomerBalanceFilter.Overdue;
                    break;
                default:
                    errors["balanceStatus"] = ["Choose any, none, has_balance or overdue."];
                    break;
            }
        }

        var sort = CustomerSort.LastActivity;

        if (!string.IsNullOrEmpty(query.Sort))
        {
            switch (query.Sort)
            {
                case "last_activity":
                    break;
                case "name_asc":
                    sort = CustomerSort.NameAsc;
                    break;
                case "name_desc":
                    sort = CustomerSort.NameDesc;
                    break;
                case "newest":
                    sort = CustomerSort.Newest;
                    break;
                case "balance_desc":
                    sort = CustomerSort.BalanceDesc;
                    break;
                default:
                    errors["sort"] = ["Choose a valid sort."];
                    break;
            }
        }

        var page = 1;

        if (!string.IsNullOrEmpty(query.Page)
            && (!int.TryParse(query.Page, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors["page"] = ["Page must be 1 or more."];
        }

        return errors.Count > 0
            ? null
            : new CustomerListFilter(
                tab,
                string.IsNullOrEmpty(search) ? null : search,
                type,
                branchId,
                [.. tagIds.Distinct()],
                balance,
                sort,
                page);
    }
}
