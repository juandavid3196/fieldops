namespace FieldOps.Domain.Customers;

public sealed class CustomerContact
{
    private CustomerContact()
    {
    }

    private CustomerContact(
        Guid id,
        Guid organizationId,
        Guid customerId,
        string firstName)
    {
        Id = id;
        OrganizationId = organizationId;
        CustomerId = customerId;
        FirstName = firstName;
        IsPrimary = false;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid CustomerId { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string? LastName { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? Title { get; private set; }

    public bool IsPrimary { get; private set; }

    public Guid? PortalUserId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static CustomerContact Create(
        Guid organizationId,
        Guid customerId,
        string firstName)
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

        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException(
                "Contact first name is required.",
                nameof(firstName));
        }

        return new CustomerContact(
            Guid.NewGuid(),
            organizationId,
            customerId,
            firstName.Trim());
    }
}
