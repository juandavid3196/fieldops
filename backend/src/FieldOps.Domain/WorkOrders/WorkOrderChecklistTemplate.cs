namespace FieldOps.Domain.WorkOrders;

public sealed class WorkOrderChecklistTemplate
{
    private WorkOrderChecklistTemplate()
    {
    }

    private WorkOrderChecklistTemplate(Guid id, Guid workOrderId, string label)
    {
        Id = id;
        WorkOrderId = workOrderId;
        Label = label;
        IsRequired = true;
        SortOrder = 0;
    }

    public Guid Id { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public bool IsRequired { get; private set; }

    public int SortOrder { get; private set; }

    public static WorkOrderChecklistTemplate Create(Guid workOrderId, string label)
    {
        if (workOrderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Work order id is required.",
                nameof(workOrderId));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException(
                "Checklist template label is required.",
                nameof(label));
        }

        return new WorkOrderChecklistTemplate(Guid.NewGuid(), workOrderId, label.Trim());
    }
}
