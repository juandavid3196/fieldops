namespace FieldOps.Domain.Customers;

public sealed class Customer
{
    private Customer()
    {
    }

    private Customer(
        Guid id,
        Guid organizationId,
        Guid branchId,
        CustomerType type,
        string displayName)
    {
        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        Type = type;
        DisplayName = displayName;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid BranchId { get; private set; }

    public CustomerType Type { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string? LegalName { get; private set; }

    public string? TaxId { get; private set; }

    public string? PrimaryEmail { get; private set; }

    public string? PrimaryPhone { get; private set; }

    public string? BillingAddress { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Customer Create(
        Guid organizationId,
        Guid branchId,
        CustomerType type,
        string displayName,
        string? primaryEmail = null,
        string? primaryPhone = null,
        string? notes = null)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (branchId == Guid.Empty)
        {
            throw new ArgumentException(
                "Branch id is required.",
                nameof(branchId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "Customer display name is required.",
                nameof(displayName));
        }

        return new Customer(
            Guid.NewGuid(),
            organizationId,
            branchId,
            type,
            displayName.Trim())
        {
            PrimaryEmail = NullIfBlank(primaryEmail),
            PrimaryPhone = NullIfBlank(primaryPhone),
            Notes = NullIfBlank(notes),
        };
    }

    /// <summary>
    /// Applies the editable fields, including the type. Returns true, and stamps
    /// <paramref name="now"/>, only when a value changed.
    /// </summary>
    public bool Update(
        Guid branchId,
        CustomerType type,
        string displayName,
        string? primaryEmail,
        string? primaryPhone,
        string? notes,
        DateTimeOffset now)
    {
        if (branchId == Guid.Empty)
        {
            throw new ArgumentException(
                "Branch id is required.",
                nameof(branchId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "Customer display name is required.",
                nameof(displayName));
        }

        var name = displayName.Trim();
        var email = NullIfBlank(primaryEmail);
        var phone = NullIfBlank(primaryPhone);
        var note = NullIfBlank(notes);

        if (BranchId == branchId
            && Type == type
            && string.Equals(DisplayName, name, StringComparison.Ordinal)
            && string.Equals(PrimaryEmail, email, StringComparison.Ordinal)
            && string.Equals(PrimaryPhone, phone, StringComparison.Ordinal)
            && string.Equals(Notes, note, StringComparison.Ordinal))
        {
            return false;
        }

        BranchId = branchId;
        Type = type;
        DisplayName = name;
        PrimaryEmail = email;
        PrimaryPhone = phone;
        Notes = note;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Stamps <c>updated_at</c> when a dependent record (contact, property, tags) changed.</summary>
    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    /// <summary>Archives or reactivates. Returns false when the state is already the requested one.</summary>
    public bool SetActive(bool isActive, DateTimeOffset now)
    {
        if (IsActive == isActive)
        {
            return false;
        }

        IsActive = isActive;
        UpdatedAt = now;

        return true;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
