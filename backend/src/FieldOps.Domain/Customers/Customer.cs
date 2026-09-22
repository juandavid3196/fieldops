namespace FieldOps.Domain.Customers;

public sealed class Customer
{
    private Customer()
    {
    }

    private Customer(
        Guid id,
        Guid organizationId,
        CustomerType type,
        string displayName)
    {
        Id = id;
        OrganizationId = organizationId;
        Type = type;
        DisplayName = displayName;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

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
        CustomerType type,
        string displayName)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
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
            type,
            displayName.Trim());
    }
}
