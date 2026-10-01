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
        string? serviceNotes = null)
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
            StateRegion = NullIfBlank(stateRegion),
            PostalCode = NullIfBlank(postalCode),
            ServiceNotes = NullIfBlank(serviceNotes),
        };
    }

    /// <summary>
    /// Applies the editable fields of the first property (the country is kept). Returns true, and
    /// stamps <paramref name="now"/>, only when a value changed.
    /// </summary>
    public bool Update(
        Guid branchId,
        string addressLine1,
        string city,
        string? stateRegion,
        string? postalCode,
        string? serviceNotes,
        DateTimeOffset now)
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

        var address = addressLine1.Trim();
        var cityValue = city.Trim();
        var state = NullIfBlank(stateRegion);
        var postal = NullIfBlank(postalCode);
        var notes = NullIfBlank(serviceNotes);

        if (BranchId == branchId
            && string.Equals(AddressLine1, address, StringComparison.Ordinal)
            && string.Equals(City, cityValue, StringComparison.Ordinal)
            && string.Equals(StateRegion, state, StringComparison.Ordinal)
            && string.Equals(PostalCode, postal, StringComparison.Ordinal)
            && string.Equals(ServiceNotes, notes, StringComparison.Ordinal))
        {
            return false;
        }

        BranchId = branchId;
        AddressLine1 = address;
        City = cityValue;
        StateRegion = state;
        PostalCode = postal;
        ServiceNotes = notes;
        UpdatedAt = now;

        return true;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
