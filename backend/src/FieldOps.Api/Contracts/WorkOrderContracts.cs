using FieldOps.Application.Features.ChecklistTemplates;
using FieldOps.Application.Features.WorkOrders;

namespace FieldOps.Api.Contracts;

public sealed record WorkOrderTaskBody(string? Label);

public sealed record WorkOrderMaterialBody(
    Guid? QuoteLineId,
    Guid? CatalogItemId,
    string? Description,
    decimal? Quantity,
    string? Unit,
    string? Source);

public sealed record WorkOrderRecurrenceBody(string? Frequency, int? Count);

public sealed record WorkOrderCommunicationBody(
    bool? NotifyCustomerWhenScheduled,
    bool? SendTechnicianDetails,
    bool? SendArrivalReminder);

/// <summary>
/// Body of PUT /quotes/{id}/work-order/draft and POST /quotes/{id}/work-order (API contracts WorkOrderBody plus the
/// concurrency token). Unknown keys, such as client-sent prices or an organization id, are ignored (BR-17).
/// </summary>
public sealed record WorkOrderRequestBody(
    string? Title,
    string? JobType,
    Guid? ServiceCategoryId,
    Guid? BranchId,
    string? Priority,
    int? EstimatedDurationMinutes,
    List<Guid>? SkillIds,
    List<WorkOrderTaskBody?>? Tasks,
    List<WorkOrderMaterialBody?>? Materials,
    string? Instructions,
    string? PreferredDate,
    string? ArrivalWindow,
    WorkOrderRecurrenceBody? Recurrence,
    WorkOrderCommunicationBody? Communication,
    string? UpdatedAt)
{
    public WorkOrderText ToText() =>
        new(
            Title,
            JobType,
            ServiceCategoryId,
            BranchId,
            Priority,
            EstimatedDurationMinutes,
            SkillIds,
            Tasks?.Select(task => task is null ? null : new WorkOrderTaskText(task.Label)).ToList(),
            Materials?.Select(material => material is null
                    ? null
                    : new WorkOrderMaterialText(
                        material.QuoteLineId,
                        material.CatalogItemId,
                        material.Description,
                        material.Quantity,
                        material.Unit,
                        material.Source))
                .ToList(),
            Instructions,
            PreferredDate,
            ArrivalWindow,
            Recurrence is null ? null : new WorkOrderRecurrenceText(Recurrence.Frequency, Recurrence.Count),
            Communication is null
                ? null
                : new WorkOrderCommunicationText(
                    Communication.NotifyCustomerWhenScheduled,
                    Communication.SendTechnicianDetails,
                    Communication.SendArrivalReminder));
}

public sealed record ChecklistItemBody(string? Label);

/// <summary>Body of POST /checklist-templates (BR-11).</summary>
public sealed record ChecklistTemplateBody(string? Name, Guid? ServiceCategoryId, List<ChecklistItemBody?>? Items)
{
    public ChecklistTemplateText ToText() =>
        new(Name, ServiceCategoryId, Items?.Select(item => item is null ? null : new ChecklistItemText(item.Label)).ToList());
}
