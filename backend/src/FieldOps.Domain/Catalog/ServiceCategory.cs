namespace FieldOps.Domain.Catalog;

public sealed class ServiceCategory
{
    public const int NameMaxLength = 120;

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

    /// <summary>Renames to the trimmed name; returns false when the stored name is identical.</summary>
    public bool Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Service category name is required.", nameof(name));
        }

        var trimmed = name.Trim();

        if (string.Equals(Name, trimmed, StringComparison.Ordinal))
        {
            return false;
        }

        Name = trimmed;

        return true;
    }

    /// <summary>Changes the active flag; returns false when it already has that value.</summary>
    public bool SetActive(bool isActive)
    {
        if (IsActive == isActive)
        {
            return false;
        }

        IsActive = isActive;

        return true;
    }
}
