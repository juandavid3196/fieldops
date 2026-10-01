namespace FieldOps.Domain.Customers;

/// <summary>An organization tag a customer can carry. Names are unique per organization, case-insensitively.</summary>
public sealed class CustomerTag
{
    public const int NameMaxLength = 40;

    private CustomerTag()
    {
    }

    private CustomerTag(Guid id, Guid organizationId, string name)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        NormalizedName = NormalizeName(name);
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static CustomerTag Create(Guid organizationId, string name)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0 || trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException(
                "Tag name must be 1 to 40 characters.",
                nameof(name));
        }

        return new CustomerTag(Guid.NewGuid(), organizationId, trimmed);
    }

    /// <summary>The comparison key: the trimmed, lower-cased name.</summary>
    public static string NormalizeName(string? name) =>
        (name ?? string.Empty).Trim().ToLowerInvariant();
}
