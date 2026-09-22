namespace FieldOps.Domain.Customers;

public sealed class CustomerNote
{
    private CustomerNote()
    {
    }

    private CustomerNote(
        Guid id,
        Guid organizationId,
        Guid customerId,
        Guid authorUserId,
        string note)
    {
        Id = id;
        OrganizationId = organizationId;
        CustomerId = customerId;
        AuthorUserId = authorUserId;
        Note = note;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string Note { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static CustomerNote Create(
        Guid organizationId,
        Guid customerId,
        Guid authorUserId,
        string note)
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

        if (authorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Author user id is required.",
                nameof(authorUserId));
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException(
                "Note text is required.",
                nameof(note));
        }

        return new CustomerNote(
            Guid.NewGuid(),
            organizationId,
            customerId,
            authorUserId,
            note.Trim());
    }
}
