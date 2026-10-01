namespace FieldOps.Domain.Customers;

public sealed class CustomerTagAssignment
{
    private CustomerTagAssignment()
    {
    }

    private CustomerTagAssignment(Guid organizationId, Guid customerId, Guid tagId)
    {
        OrganizationId = organizationId;
        CustomerId = customerId;
        TagId = tagId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid OrganizationId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid TagId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static CustomerTagAssignment Create(Guid organizationId, Guid customerId, Guid tagId)
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

        if (tagId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tag id is required.",
                nameof(tagId));
        }

        return new CustomerTagAssignment(organizationId, customerId, tagId);
    }
}
