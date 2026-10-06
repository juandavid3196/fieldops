using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.WorkOrders;

/// <summary>Messages, limits and machine codes of the work order creation (create-work-order BR-05 to BR-20).</summary>
public static class WorkOrderMessages
{
    public const int MaxTasks = 50;

    public const int MaxMaterials = 50;

    public const int MaxSkills = 10;

    public const string QuoteNotApprovedCode = "quote_not_approved";

    public const string CustomerRequiredCode = "customer_required";

    public const string WorkOrderChangedCode = "work_order_changed";

    public const string WorkOrderCreatedCode = "work_order_created";

    public const string QuoteNotApprovedTitle = "This quote is no longer approved.";

    public const string CustomerRequiredTitle = "Link a customer and property to this request before creating a work order.";

    public const string WorkOrderChangedTitle = "This work order was changed by someone else. Reload to see the latest version.";

    public const string WorkOrderCreatedTitle = "This work order has already been created.";

    public const string RequestChangedTitle = "The request changed. Reload and try again.";

    public const string TitleRequired = "Enter a work order title.";

    public const string TitleTooLong = "Title must be 160 characters or fewer.";

    public const string JobTypeMessage = "Select a job type.";

    public const string CategoryMessage = "Select a service category.";

    public const string BranchMessage = "Select a branch.";

    public const string PriorityMessage = "Select a priority.";

    public const string DurationMessage = "Select a valid duration.";

    public const string SkillsMessage = "Select up to 10 skills.";

    public const string SkillIdsInvalidMessage = "Select valid skills.";

    public const string TasksRequiredMessage = "Add at least one task.";

    public const string TasksTooManyMessage = "A work order can have up to 50 tasks.";

    public const string TaskRequiredMessage = "Enter a task.";

    public const string TaskTooLongMessage = "Task must be 240 characters or fewer.";

    public const string MaterialsTooManyMessage = "A work order can have up to 50 materials.";

    public const string MaterialRequiredMessage = "Enter a material.";

    public const string MaterialTooLongMessage = "Material must be 240 characters or fewer.";

    public const string QuantityMessage = "Enter a quantity greater than 0.";

    public const string UnitMessage = "Enter a unit.";

    public const string SourceMessage = "Select a source.";

    public const string CatalogItemMessage = "This catalog item isn't available.";

    public const string QuoteLineMessage = "This quote line isn't available.";

    public const string InstructionsTooLong = "Instructions must be 2000 characters or fewer.";

    public const string PreferredDateMessage = "Select today or a later date.";

    public const string WindowNeedsDateMessage = "Select a preferred date first.";

    public const string WindowMessage = "Select a valid arrival window.";

    public const string RecurrenceFrequencyMessage = "Select how often this job repeats.";

    public const string RecurrenceCountMessage = "Enter between 2 and 24 occurrences.";

    public const string RecurrenceNotAllowedMessage = "A one-time job does not repeat.";

    public const string CommunicationMessage = "Select an option.";

    public const string UpdatedAtMessage = "Enter a valid value.";

    public static string ConflictTitle(string code) => code switch
    {
        QuoteNotApprovedCode => QuoteNotApprovedTitle,
        CustomerRequiredCode => CustomerRequiredTitle,
        WorkOrderChangedCode => WorkOrderChangedTitle,
        WorkOrderCreatedCode => WorkOrderCreatedTitle,
        ServiceRequestMessages.RequestChangedCode => RequestChangedTitle,
        _ => ServiceRequestMessages.ConflictTitle,
    };
}

/// <summary>Codes and mappings of the work order enumerations.</summary>
public static class WorkOrderCodes
{
    public static readonly string[] Frequencies = ["weekly", "biweekly", "monthly", "quarterly"];

    public static readonly string[] Sources = ["truck_stock", "warehouse", "to_purchase"];

    public static short? PriorityValue(string? code) => code switch
    {
        "urgent" => 1,
        "high" => 2,
        "normal" => 3,
        "low" => 4,
        _ => null,
    };

    public static string PriorityCode(short value) => value switch
    {
        1 => "urgent",
        2 => "high",
        3 => "normal",
        _ => "low",
    };

    public static string PriorityFromUrgency(string urgency) => urgency switch
    {
        "emergency" => "urgent",
        "urgent" => "high",
        _ => "normal",
    };

    public static string StatusCode(FieldOps.Domain.WorkOrders.WorkOrderStatus status) => SnakeCase(status.ToString());

    public static string VisitStatusCode(FieldOps.Domain.WorkOrders.VisitStatus status) => SnakeCase(status.ToString());

    private static string SnakeCase(string name) =>
        string.Concat(name.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? "_" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
}

public sealed record WorkOrderTaskValue(string Label);

public sealed record WorkOrderMaterialValue(
    Guid? QuoteLineId,
    Guid? CatalogItemId,
    string Description,
    decimal Quantity,
    string Unit,
    string Source);

public sealed record WorkOrderRecurrence(string Frequency, int Count);

public sealed record WorkOrderCommunication(
    bool NotifyCustomerWhenScheduled,
    bool SendTechnicianDetails,
    bool SendArrivalReminder);

/// <summary>The work order form values (API contracts WorkOrderBody, without the concurrency token).</summary>
public sealed record WorkOrderValues(
    string Title,
    string JobType,
    Guid? ServiceCategoryId,
    Guid? BranchId,
    string Priority,
    int? EstimatedDurationMinutes,
    IReadOnlyList<Guid> SkillIds,
    IReadOnlyList<WorkOrderTaskValue> Tasks,
    IReadOnlyList<WorkOrderMaterialValue> Materials,
    string? Instructions,
    DateOnly? PreferredDate,
    string ArrivalWindow,
    WorkOrderRecurrence? Recurrence,
    WorkOrderCommunication Communication);

public sealed record EditorQuote(Guid Id, string DisplayNumber, DateTimeOffset ApprovedAt, decimal ApprovedTotal, string Currency);

public sealed record EditorCustomer(string Name, string? Phone, string? Email, string Address);

public sealed record EditorPhoto(Guid Id);

public sealed record EditorAssessment(Guid Id, string? TechnicianName, string? Diagnosis, IReadOnlyList<EditorPhoto> Photos);

public sealed record EditorWorkOrder(Guid Id, string DisplayNumber, string Status, DateTimeOffset UpdatedAt);

public sealed record EditorBranch(Guid Id, string Name, string Timezone);

public sealed record EditorOption(Guid Id, string Name);

public sealed record EditorOptions(
    IReadOnlyList<EditorBranch> Branches,
    IReadOnlyList<EditorOption> Categories,
    IReadOnlyList<EditorOption> Skills);

/// <summary>API contracts WorkOrderEditor: the locked context, the saved or prefilled values and the options.</summary>
public sealed record WorkOrderEditor(
    EditorQuote Quote,
    Guid RequestId,
    EditorCustomer Customer,
    string? AccessNote,
    EditorAssessment? Assessment,
    EditorWorkOrder? WorkOrder,
    WorkOrderValues Values,
    EditorOptions Options,
    string OrganizationTimezone);

public sealed record WorkOrderDraftSaved(WorkOrderEditor Editor, bool Inserted);

/// <summary>The identity of a work order: <c>{ id, displayNumber }</c>; <c>Created</c> is false for an idempotent repeat.</summary>
public sealed record WorkOrderRef(Guid Id, string DisplayNumber);

public sealed record WorkOrderCreated(WorkOrderRef Order, bool Created);

public sealed record WorkOrderListItem(
    Guid Id,
    string DisplayNumber,
    string Title,
    string CustomerName,
    string BranchName,
    string Priority,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record WorkOrderPage(IReadOnlyList<WorkOrderListItem> Items, int Total);

public sealed record JobQuote(Guid Id, string DisplayNumber, decimal ApprovedTotal, string Currency);

public sealed record JobCustomer(Guid Id, string Name);

public sealed record JobBranch(Guid Id, string Name, string Timezone);

public sealed record JobVisit(int VisitNumber, string Status);

/// <summary>API contracts WorkOrderDetail (read-only). Instants are in the organization zone; the preferred date and window follow the branch zone.</summary>
public sealed record WorkOrderDetail(
    Guid Id,
    string DisplayNumber,
    string Status,
    DateTimeOffset UpdatedAt,
    bool CanManage,
    JobQuote Quote,
    JobCustomer Customer,
    string? PropertyAddress,
    string? AccessNote,
    string JobType,
    EditorOption Category,
    JobBranch Branch,
    string Priority,
    int? EstimatedDurationMinutes,
    IReadOnlyList<EditorOption> Skills,
    WorkOrderRecurrence? Recurrence,
    DateTimeOffset? PreferredStart,
    DateTimeOffset? PreferredEnd,
    string ArrivalWindow,
    DateOnly? PreferredDate,
    IReadOnlyList<WorkOrderTaskValue> Tasks,
    IReadOnlyList<WorkOrderMaterialValue> Materials,
    string? Instructions,
    WorkOrderCommunication Communication,
    IReadOnlyList<JobVisit> Visits);

/// <summary>The visibility of a quote and its created order, before any lock (404 and the idempotent repeat).</summary>
public sealed record WorkOrderLookup(bool Visible, WorkOrderRef? Created);

public abstract record WorkOrderOutcome<T>
{
    private WorkOrderOutcome()
    {
    }

    public sealed record Succeeded(T Value) : WorkOrderOutcome<T>;

    /// <summary>A missing, foreign or out-of-scope quote or work order: one identical outcome.</summary>
    public sealed record NotFound : WorkOrderOutcome<T>;

    public sealed record Conflict(string Code) : WorkOrderOutcome<T>;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : WorkOrderOutcome<T>;
}

/// <summary>Persistence port of the work order creation. Every method is scoped to one organization and the caller's branch scope.</summary>
public interface IWorkOrderStore
{
    Task<WorkOrderLookup> LookupAsync(Guid organizationId, BranchScope scope, Guid quoteId, CancellationToken cancellationToken);

    Task<WorkOrderOutcome<WorkOrderEditor>> GetEditorAsync(
        Guid organizationId, BranchScope scope, Guid quoteId, CancellationToken cancellationToken);

    Task<WorkOrderOutcome<WorkOrderDraftSaved>> SaveDraftAsync(
        QuoteActor actor, Guid quoteId, DateTimeOffset? updatedAt, WorkOrderInput input, CancellationToken cancellationToken);

    Task<WorkOrderOutcome<WorkOrderCreated>> CreateAsync(
        QuoteActor actor, Guid quoteId, DateTimeOffset? updatedAt, WorkOrderInput input, CancellationToken cancellationToken);

    Task<WorkOrderPage> ListAsync(
        Guid organizationId, BranchScope scope, int page, int pageSize, CancellationToken cancellationToken);

    Task<WorkOrderDetail?> GetAsync(
        Guid organizationId, BranchScope scope, Guid workOrderId, bool canManage, CancellationToken cancellationToken);
}
