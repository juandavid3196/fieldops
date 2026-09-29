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

    public bool IsMain { get; private set; }

    public string[] ServicePostalCodes { get; private set; } = [];

    public bool UsesCompanyBilling { get; private set; } = true;

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
        string? addressLine2,
        string? city,
        string? stateRegion,
        string? postalCode,
        string? countryCode,
        string? timezone,
        string businessHours,
        bool isMain = false,
        string[]? servicePostalCodes = null,
        bool usesCompanyBilling = true)
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
            AddressLine2 = addressLine2?.Trim(),
            City = city?.Trim(),
            StateRegion = stateRegion?.Trim(),
            PostalCode = postalCode?.Trim(),
            CountryCode = countryCode?.Trim(),
            Timezone = timezone?.Trim(),
            BusinessHours = businessHours,
            IsMain = isMain,
            ServicePostalCodes = servicePostalCodes ?? [],
            UsesCompanyBilling = usesCompanyBilling,
        };
    }

    /// <summary>
    /// Updates the branch's profile, contact, address, time zone and
    /// business hours (BR-03). Same invariants, trimming and
    /// <see cref="ArgumentException"/> pattern as <see cref="Create"/>. May be
    /// called on an inactive branch (FR-08). <paramref name="updatedAt"/> is
    /// the new <c>updated_at</c> value, supplied by the caller so the entity
    /// never reads the system clock.
    /// </summary>
    public void UpdateDetails(
        string name,
        string code,
        string? email,
        string? phone,
        string? addressLine1,
        string? addressLine2,
        string? city,
        string? stateRegion,
        string? postalCode,
        string? countryCode,
        string? timezone,
        string businessHours,
        string[] servicePostalCodes,
        bool usesCompanyBilling,
        DateTimeOffset updatedAt)
    {
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

        Name = name.Trim();
        Code = code.Trim();
        Email = email?.Trim();
        Phone = phone?.Trim();
        AddressLine1 = addressLine1?.Trim();
        AddressLine2 = addressLine2?.Trim();
        City = city?.Trim();
        StateRegion = stateRegion?.Trim();
        PostalCode = postalCode?.Trim();
        CountryCode = countryCode?.Trim();
        Timezone = timezone?.Trim();
        BusinessHours = businessHours;
        ServicePostalCodes = servicePostalCodes ?? [];
        UsesCompanyBilling = usesCompanyBilling;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Deactivates the branch (BR-06 is enforced by the caller, not here).
    /// Returns <c>false</c> without changing anything when already inactive
    /// (BR-08 no-op).
    /// </summary>
    public bool Deactivate(DateTimeOffset updatedAt)
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        UpdatedAt = updatedAt;
        return true;
    }

    /// <summary>
    /// Reactivates the branch. Returns <c>false</c> without changing
    /// anything when already active (BR-08 no-op).
    /// </summary>
    public bool Reactivate(DateTimeOffset updatedAt)
    {
        if (IsActive)
        {
            return false;
        }

        IsActive = true;
        UpdatedAt = updatedAt;
        return true;
    }

    /// <summary>
    /// Marks the branch as the organization's main branch (BR-11). Only an
    /// active branch can be main; the caller clears the previous main first.
    /// Returns <c>false</c> without changing anything when already main.
    /// </summary>
    public bool SetMain(DateTimeOffset updatedAt)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Only an active branch can be the main branch.");
        }

        if (IsMain)
        {
            return false;
        }

        IsMain = true;
        UpdatedAt = updatedAt;
        return true;
    }

    /// <summary>Clears the main flag. Returns <c>false</c> when it was not main.</summary>
    public bool ClearMain(DateTimeOffset updatedAt)
    {
        if (!IsMain)
        {
            return false;
        }

        IsMain = false;
        UpdatedAt = updatedAt;
        return true;
    }
}
