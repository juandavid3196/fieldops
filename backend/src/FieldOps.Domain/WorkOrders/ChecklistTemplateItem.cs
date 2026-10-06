namespace FieldOps.Domain.WorkOrders;

public sealed class ChecklistTemplateItem
{
    private ChecklistTemplateItem()
    {
    }

    private ChecklistTemplateItem(
        Guid id,
        Guid organizationId,
        Guid templateId,
        string label,
        int sortOrder)
    {
        Id = id;
        OrganizationId = organizationId;
        TemplateId = templateId;
        Label = label;
        SortOrder = sortOrder;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid TemplateId { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public static ChecklistTemplateItem Create(
        Guid organizationId,
        Guid templateId,
        string label,
        int sortOrder)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (templateId == Guid.Empty)
        {
            throw new ArgumentException(
                "Template id is required.",
                nameof(templateId));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException(
                "Label is required.",
                nameof(label));
        }

        return new ChecklistTemplateItem(Guid.NewGuid(), organizationId, templateId, label.Trim(), sortOrder);
    }
}
