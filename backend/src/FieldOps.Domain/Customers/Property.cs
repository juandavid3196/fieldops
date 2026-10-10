namespace FieldOps.Domain.Customers;

public sealed class Property
{
    private Property()
    {
    }

    private Property(
        Guid id,
        Guid organizationId,
        Guid customerId,
        string name,
        string addressLine1,
        string city,
        string countryCode)
    {
        Id = id;
        OrganizationId = organizationId;
        CustomerId = customerId;
        Name = name;
        AddressLine1 = addressLine1;
        City = city;
        CountryCode = countryCode;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid? BranchId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string AddressLine1 { get; private set; } = string.Empty;

    public string? AddressLine2 { get; private set; }

    public string City { get; private set; } = string.Empty;

    public string? StateRegion { get; private set; }

    public string? PostalCode { get; private set; }

    public string CountryCode { get; private set; } = string.Empty;

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    public string? AccessInstructions { get; private set; }

    public string? ServiceNotes { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsPrimary { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Property Create(
        Guid organizationId,
        Guid customerId,
        string name,
        string addressLine1,
        string city,
        string countryCode,
        Guid? branchId = null,
        string? stateRegion = null,
        string? postalCode = null,
        string? serviceNotes = null,
        bool isPrimary = false,
        string? addressLine2 = null,
        string? accessInstructions = null)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Customer id is required.",
                nameof(customerId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Property name is required.",
                nameof(name));
        }

        if (string.IsNullOrWhiteSpace(addressLine1))
        {
            throw new ArgumentException(
                "Property address line 1 is required.",
                nameof(addressLine1));
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            throw new ArgumentException(
                "Property city is required.",
                nameof(city));
        }

        if (string.IsNullOrWhiteSpace(countryCode))
        {
            throw new ArgumentException(
                "Property country code is required.",
                nameof(countryCode));
        }

        return new Property(
            Guid.NewGuid(),
            organizationId,
            customerId,
            name.Trim(),
            addressLine1.Trim(),
            city.Trim(),
            countryCode.Trim())
        {
            BranchId = branchId,
            IsPrimary = isPrimary,
            AddressLine2 = NullIfBlank(addressLine2),
            StateRegion = NullIfBlank(stateRegion),
            PostalCode = NullIfBlank(postalCode),
            ServiceNotes = NullIfBlank(serviceNotes),
            AccessInstructions = NullIfBlank(accessInstructions),
        };
    }

    /// <summary>
    /// Applies the address and service instructions of the primary property edited from the customer
    /// drawer (BR-08); the name and branch are never touched. Returns true, and stamps
    /// <paramref name="now"/>, only when a value changed.
    /// </summary>
    public bool UpdateAddress(
        string addressLine1,
        string city,
        string? stateRegion,
        string? postalCode,
        string? serviceNotes,
        DateTimeOffset now)
    {
        RequireAddress(addressLine1, city);

        var address = addressLine1.Trim();
        var cityValue = city.Trim();
        var state = NullIfBlank(stateRegion);
        var postal = NullIfBlank(postalCode);
        var notes = NullIfBlank(serviceNotes);

        if (string.Equals(AddressLine1, address, StringComparison.Ordinal)
            && string.Equals(City, cityValue, StringComparison.Ordinal)
            && string.Equals(StateRegion, state, StringComparison.Ordinal)
            && string.Equals(PostalCode, postal, StringComparison.Ordinal)
            && string.Equals(ServiceNotes, notes, StringComparison.Ordinal))
        {
            return false;
        }

        AddressLine1 = address;
        City = cityValue;
        StateRegion = state;
        PostalCode = postal;
        ServiceNotes = notes;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// Applies every editable field of an active property (BR-05). Returns the request names of the
    /// fields that changed (empty for a no-op, which leaves <see cref="UpdatedAt"/> untouched).
    /// </summary>
    public IReadOnlyList<string> Edit(
        string name,
        string addressLine1,
        string? addressLine2,
        string city,
        string? stateRegion,
        string? postalCode,
        Guid branchId,
        string? serviceNotes,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Property name is required.", nameof(name));
        }

        RequireAddress(addressLine1, city);

        var changed = new List<string>();
        var nameValue = name.Trim();
        var address = addressLine1.Trim();
        var address2 = NullIfBlank(addressLine2);
        var cityValue = city.Trim();
        var state = NullIfBlank(stateRegion);
        var postal = NullIfBlank(postalCode);
        var notes = NullIfBlank(serviceNotes);

        void Track(string field, string? before, string? after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                changed.Add(field);
            }
        }

        Track("name", Name, nameValue);
        Track("addressLine1", AddressLine1, address);
        Track("addressLine2", AddressLine2, address2);
        Track("city", City, cityValue);
        Track("stateRegion", StateRegion, state);
        Track("postalCode", PostalCode, postal);

        if (BranchId != branchId)
        {
            changed.Add("branchId");
        }

        Track("serviceInstructions", ServiceNotes, notes);

        if (changed.Count == 0)
        {
            return changed;
        }

        Name = nameValue;
        AddressLine1 = address;
        AddressLine2 = address2;
        City = cityValue;
        StateRegion = state;
        PostalCode = postal;
        BranchId = branchId;
        ServiceNotes = notes;
        UpdatedAt = now;

        return changed;
    }

    /// <summary>
    /// Applies the only fields a portal contact may edit (customer portal BR-33): the name and the access instructions.
    /// Returns the request names of the fields that changed (empty for a no-op, which leaves <see cref="UpdatedAt"/> untouched).
    /// </summary>
    public IReadOnlyList<string> EditFromPortal(
        bool setName, string? name, bool setAccessInstructions, string? accessInstructions, DateTimeOffset now)
    {
        var changed = new List<string>();

        if (setName)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Property name is required.", nameof(name));
            }

            if (!string.Equals(Name, name.Trim(), StringComparison.Ordinal))
            {
                Name = name.Trim();
                changed.Add("name");
            }
        }

        if (setAccessInstructions)
        {
            var instructions = NullIfBlank(accessInstructions);

            if (!string.Equals(AccessInstructions, instructions, StringComparison.Ordinal))
            {
                AccessInstructions = instructions;
                changed.Add("accessInstructions");
            }
        }

        if (changed.Count > 0)
        {
            UpdatedAt = now;
        }

        return changed;
    }

    /// <summary>Makes an active, non-primary property primary (BR-07); any other state is reported, not changed.</summary>
    public PropertyTransition SetPrimary(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return PropertyTransition.NotActive;
        }

        if (IsPrimary)
        {
            return PropertyTransition.AlreadyPrimary;
        }

        IsPrimary = true;
        UpdatedAt = now;

        return PropertyTransition.Applied;
    }

    /// <summary>Archives an active, non-primary property (BR-07); any other state is reported, not changed.</summary>
    public PropertyTransition Archive(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return PropertyTransition.AlreadyArchived;
        }

        if (IsPrimary)
        {
            return PropertyTransition.PrimaryCannotBeArchived;
        }

        IsActive = false;
        UpdatedAt = now;

        return PropertyTransition.Applied;
    }

    /// <summary>Reactivates an archived property as active and non-primary (BR-07).</summary>
    public PropertyTransition Reactivate(DateTimeOffset now)
    {
        if (IsActive)
        {
            return PropertyTransition.AlreadyActive;
        }

        IsActive = true;
        IsPrimary = false;
        UpdatedAt = now;

        return PropertyTransition.Applied;
    }

    private static void RequireAddress(string addressLine1, string city)
    {
        if (string.IsNullOrWhiteSpace(addressLine1))
        {
            throw new ArgumentException(
                "Property address line 1 is required.",
                nameof(addressLine1));
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            throw new ArgumentException(
                "Property city is required.",
                nameof(city));
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Result of a property state change guard (BR-07).</summary>
public enum PropertyTransition
{
    Applied,
    AlreadyPrimary,
    NotActive,
    PrimaryCannotBeArchived,
    AlreadyArchived,
    AlreadyActive,
}
