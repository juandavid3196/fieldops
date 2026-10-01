namespace FieldOps.Application.Features.Customers;

/// <summary>Field validation of properties and notes (BR-05, BR-16). The branch is checked by the handler.</summary>
public static class CustomerPropertyRules
{
    public const string NameKey = "name";

    public const string AddressLine2Key = "addressLine2";

    public const string NoteKey = "note";

    public const int NoteMaxLength = 2000;

    /// <summary>The values with the branch still unresolved (<see cref="Guid.Empty"/>), or null with one message per key.</summary>
    public static CustomerPropertyValues? Validate(
        CustomerPropertyInput input,
        bool unitedStates,
        out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var name = input.Name?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            errors[NameKey] = [CustomerMessages.PropertyNameRequired];
        }
        else if (name.Length > 140)
        {
            errors[NameKey] = [CustomerMessages.PropertyNameTooLong];
        }

        var address = input.AddressLine1?.Trim() ?? string.Empty;

        if (address.Length == 0)
        {
            errors[CustomerRules.AddressLine1Key] = [CustomerMessages.AddressRequired];
        }
        else if (address.Length > 180)
        {
            errors[CustomerRules.AddressLine1Key] = [CustomerMessages.AddressTooLong];
        }

        var address2 = CustomerRules.Blank(input.AddressLine2);

        if (address2 is { Length: > 180 })
        {
            errors[AddressLine2Key] = [CustomerMessages.AddressTooLong];
        }

        var city = input.City?.Trim() ?? string.Empty;

        if (city.Length == 0)
        {
            errors[CustomerRules.CityKey] = [CustomerMessages.CityRequired];
        }
        else if (city.Length > 100)
        {
            errors[CustomerRules.CityKey] = [CustomerMessages.CityTooLong];
        }

        var state = CustomerRules.NormalizeState(input.StateRegion, unitedStates, out var stateError);

        if (stateError is not null)
        {
            errors[CustomerRules.StateRegionKey] = [stateError];
        }

        var postal = CustomerRules.Blank(input.PostalCode);

        if (!CustomerRules.IsValidPostal(postal, unitedStates))
        {
            errors[CustomerRules.PostalCodeKey] = [CustomerMessages.PostalInvalid];
        }

        var instructions = CustomerRules.Blank(input.ServiceInstructions);

        if (instructions is { Length: > 2000 })
        {
            errors[CustomerRules.ServiceInstructionsKey] = [CustomerMessages.TextTooLong];
        }

        return errors.Count > 0
            ? null
            : new CustomerPropertyValues(name, address, address2, city, state, postal, Guid.Empty, instructions);
    }

    /// <summary>The trimmed note, or null with the BR-16 message.</summary>
    public static string? ValidateNote(string? note, out string? error)
    {
        var text = note?.Trim() ?? string.Empty;
        error = null;

        if (text.Length == 0)
        {
            error = CustomerMessages.NoteRequired;
        }
        else if (text.Length > NoteMaxLength)
        {
            error = CustomerMessages.TextTooLong;
        }

        return error is null ? text : null;
    }

    /// <summary>A page number (default 1), or null when it is not a whole number of 1 or more.</summary>
    public static int? ParsePage(string? page)
    {
        if (string.IsNullOrEmpty(page))
        {
            return 1;
        }

        return int.TryParse(
            page,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value) && value >= 1
            ? value
            : null;
    }
}
