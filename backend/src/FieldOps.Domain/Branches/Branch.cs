namespace FieldOps.Domain.Branches;

public sealed class Branch
{
    private Branch()
    {
    }

    private Branch(Guid id, Guid organizationId, string name, string code)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        Code = code;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? AddressLine1 { get; private set; }

    public string? AddressLine2 { get; private set; }

    public string? City { get; private set; }

    public string? StateRegion { get; private set; }

    public string? PostalCode { get; private set; }

    public string? CountryCode { get; private set; }

    public string? Timezone { get; private set; }

    public string BusinessHours { get; private set; } = "{}";

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates the first branch during registration. Address, contact and
    /// time zone columns are nullable; business-required-ness for them is
    /// enforced by the caller's validator, not here. <paramref name="businessHours"/>
    /// is the final canonical JSON object (BR-15), already built by the caller.
    /// </summary>
    public static Branch Create(
        Guid organizationId,
        string name,
        string code,
        string? email,
        string? phone,
        string? addressLine1,
        string? city,
        string? stateRegion,
        string? postalCode,
        string? countryCode,
        string? timezone,
        string businessHours)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Branch name is required.",
                nameof(name));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Branch code is required.",
                nameof(code));
        }

        if (string.IsNullOrWhiteSpace(businessHours))
        {
            throw new ArgumentException(
                "Branch business hours are required.",
                nameof(businessHours));
        }

        return new Branch(
            Guid.NewGuid(),
            organizationId,
            name.Trim(),
            code.Trim())
        {
            Email = email?.Trim(),
            Phone = phone?.Trim(),
            AddressLine1 = addressLine1?.Trim(),
            City = city?.Trim(),
            StateRegion = stateRegion?.Trim(),
            PostalCode = postalCode?.Trim(),
            CountryCode = countryCode?.Trim(),
            Timezone = timezone?.Trim(),
            BusinessHours = businessHours,
        };
    }
}
