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

    public static Branch Create(Guid organizationId, string name, string code)
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

        return new Branch(
            Guid.NewGuid(),
            organizationId,
            name.Trim(),
            code.Trim());
    }
}
