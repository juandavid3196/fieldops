namespace FieldOps.Domain.Catalog;

public sealed class ServiceCategory
{
    private ServiceCategory()
    {
    }

    private ServiceCategory(Guid id, Guid organizationId, string name)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public static ServiceCategory Create(Guid organizationId, string name)
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
                "Service category name is required.",
                nameof(name));
        }

        return new ServiceCategory(Guid.NewGuid(), organizationId, name.Trim());
    }
}
