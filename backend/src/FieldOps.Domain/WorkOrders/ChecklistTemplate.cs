namespace FieldOps.Domain.WorkOrders;

/// <summary>An organization checklist template: a named list of task labels, optionally tied to a service category.</summary>
public sealed class ChecklistTemplate
{
    private ChecklistTemplate()
    {
    }

    private ChecklistTemplate(
        Guid id,
        Guid organizationId,
        Guid? serviceCategoryId,
        string name,
        Guid createdByUserId)
    {
        Id = id;
        OrganizationId = organizationId;
        ServiceCategoryId = serviceCategoryId;
        Name = name;
        CreatedByUserId = createdByUserId;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid? ServiceCategoryId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ChecklistTemplate Create(
        Guid organizationId,
        Guid? serviceCategoryId,
        string name,
        Guid createdByUserId)
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
                "Name is required.",
                nameof(name));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Created by user id is required.",
                nameof(createdByUserId));
        }

        return new ChecklistTemplate(
            Guid.NewGuid(),
            organizationId,
            serviceCategoryId,
            name.Trim(),
            createdByUserId);
    }
}
