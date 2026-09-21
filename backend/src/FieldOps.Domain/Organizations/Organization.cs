namespace FieldOps.Domain.Organizations;

public sealed class Organization
{
    private Organization()
    {
    }

    private Organization(Guid id, string name)
    {
        Id = id;
        Name = name;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? LegalName { get; private set; }

    public string? TaxId { get; private set; }

    public string Timezone { get; private set; } = "UTC";

    public string Currency { get; private set; } = "USD";

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Organization Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Organization name is required.",
                nameof(name));
        }

        return new Organization(Guid.NewGuid(), name.Trim());
    }
}
