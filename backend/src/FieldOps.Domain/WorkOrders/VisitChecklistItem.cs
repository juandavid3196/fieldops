namespace FieldOps.Domain.WorkOrders;

public sealed class VisitChecklistItem
{
    private VisitChecklistItem()
    {
    }

    private VisitChecklistItem(Guid id, Guid visitId, string label)
    {
        Id = id;
        VisitId = visitId;
        Label = label;
        IsRequired = true;
        IsCompleted = false;
        SortOrder = 0;
    }

    public Guid Id { get; private set; }

    public Guid VisitId { get; private set; }

    public Guid? TemplateItemId { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public bool IsRequired { get; private set; }

    public bool IsCompleted { get; private set; }

    public Guid? CompletedByUserId { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? Notes { get; private set; }

    public int SortOrder { get; private set; }

    public static VisitChecklistItem Create(
        Guid visitId,
        string label,
        Guid? templateItemId = null)
    {
        if (visitId == Guid.Empty)
        {
            throw new ArgumentException(
                "Visit id is required.",
                nameof(visitId));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException(
                "Checklist item label is required.",
                nameof(label));
        }

        return new VisitChecklistItem(Guid.NewGuid(), visitId, label.Trim())
        {
            TemplateItemId = templateItemId,
        };
    }
}
