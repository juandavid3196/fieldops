using System.Globalization;

namespace FieldOps.Application.Features.WorkOrders;

public sealed record WorkOrderTaskText(string? Label);

public sealed record WorkOrderMaterialText(
    Guid? QuoteLineId,
    Guid? CatalogItemId,
    string? Description,
    decimal? Quantity,
    string? Unit,
    string? Source);

public sealed record WorkOrderRecurrenceText(string? Frequency, int? Count);

public sealed record WorkOrderCommunicationText(
    bool? NotifyCustomerWhenScheduled,
    bool? SendTechnicianDetails,
    bool? SendArrivalReminder);

/// <summary>The raw work order body (API contracts WorkOrderBody) before validation; price fields are not part of it.</summary>
public sealed record WorkOrderText(
    string? Title,
    string? JobType,
    Guid? ServiceCategoryId,
    Guid? BranchId,
    string? Priority,
    int? EstimatedDurationMinutes,
    IReadOnlyList<Guid>? SkillIds,
    IReadOnlyList<WorkOrderTaskText?>? Tasks,
    IReadOnlyList<WorkOrderMaterialText?>? Materials,
    string? Instructions,
    string? PreferredDate,
    string? ArrivalWindow,
    WorkOrderRecurrenceText? Recurrence,
    WorkOrderCommunicationText? Communication);

public sealed record WorkOrderMaterialInput(
    Guid? QuoteLineId,
    Guid? CatalogItemId,
    string Description,
    decimal Quantity,
    string Unit,
    string Source);

/// <summary>A shape-validated body: normalized texts and typed values. Identifiers and the window still need the database.</summary>
public sealed record WorkOrderInput(
    string Title,
    string JobType,
    Guid ServiceCategoryId,
    Guid BranchId,
    short Priority,
    int? EstimatedDurationMinutes,
    IReadOnlyList<Guid> SkillIds,
    IReadOnlyList<string> Tasks,
    IReadOnlyList<WorkOrderMaterialInput> Materials,
    string? Instructions,
    DateOnly? PreferredDate,
    string ArrivalWindow,
    string? RecurrenceFrequency,
    short? RecurrenceCount,
    bool NotifyCustomerWhenScheduled,
    bool SendTechnicianDetails,
    bool SendArrivalReminder);

/// <summary>Shape validation of a work order body (create-work-order BR-08, BR-13); all errors at once.</summary>
public static class WorkOrderValidator
{
    public const int TitleMaxLength = 160;

    public const int TaskMaxLength = 240;

    public const int MaterialMaxLength = 240;

    public const int UnitMaxLength = 40;

    public const int InstructionsMaxLength = 2000;

    public const decimal MaxQuantity = 99_999.999m;

    /// <summary>The validated input, or null with <paramref name="errors"/> filled (keys like <c>tasks[0].label</c>).</summary>
    public static WorkOrderInput? Validate(WorkOrderText input, Dictionary<string, string[]> errors)
    {
        var before = errors.Count;

        var title = (input.Title ?? string.Empty).Trim();

        if (title.Length == 0)
        {
            errors["title"] = [WorkOrderMessages.TitleRequired];
        }
        else if (title.Length > TitleMaxLength)
        {
            errors["title"] = [WorkOrderMessages.TitleTooLong];
        }

        var jobType = input.JobType is "one_time" or "recurring" ? input.JobType : null;

        if (jobType is null)
        {
            errors["jobType"] = [WorkOrderMessages.JobTypeMessage];
        }

        if (input.ServiceCategoryId is null || input.ServiceCategoryId == Guid.Empty)
        {
            errors["serviceCategoryId"] = [WorkOrderMessages.CategoryMessage];
        }

        if (input.BranchId is null || input.BranchId == Guid.Empty)
        {
            errors["branchId"] = [WorkOrderMessages.BranchMessage];
        }

        var priority = WorkOrderCodes.PriorityValue(input.Priority);

        if (priority is null)
        {
            errors["priority"] = [WorkOrderMessages.PriorityMessage];
        }

        if (input.EstimatedDurationMinutes is { } duration && (duration is < 30 or > 720 || duration % 30 != 0))
        {
            errors["estimatedDurationMinutes"] = [WorkOrderMessages.DurationMessage];
        }

        var skillIds = ValidateSkills(input.SkillIds, errors);
        var tasks = ValidateTasks(input.Tasks, errors);
        var materials = ValidateMaterials(input.Materials, errors);

        var instructions = (input.Instructions ?? string.Empty).Trim();

        if (instructions.Length > InstructionsMaxLength)
        {
            errors["instructions"] = [WorkOrderMessages.InstructionsTooLong];
        }

        var (date, window) = ValidateSchedule(input.PreferredDate, input.ArrivalWindow, errors);
        var (frequency, count) = ValidateRecurrence(jobType, input.Recurrence, errors);

        var communication = input.Communication;

        if (communication is null)
        {
            errors["communication"] = [WorkOrderMessages.CommunicationMessage];
        }
        else
        {
            RequireFlag(communication.NotifyCustomerWhenScheduled, "communication.notifyCustomerWhenScheduled", errors);
            RequireFlag(communication.SendTechnicianDetails, "communication.sendTechnicianDetails", errors);
            RequireFlag(communication.SendArrivalReminder, "communication.sendArrivalReminder", errors);
        }

        if (errors.Count > before || jobType is null || priority is null || communication is null)
        {
            return null;
        }

        return new WorkOrderInput(
            title,
            jobType,
            input.ServiceCategoryId!.Value,
            input.BranchId!.Value,
            priority.Value,
            input.EstimatedDurationMinutes,
            skillIds,
            tasks,
            materials,
            instructions.Length == 0 ? null : instructions,
            date,
            window,
            frequency,
            count,
            communication.NotifyCustomerWhenScheduled!.Value,
            communication.SendTechnicianDetails!.Value,
            communication.SendArrivalReminder!.Value);
    }

    private static void RequireFlag(bool? value, string key, Dictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[key] = [WorkOrderMessages.CommunicationMessage];
        }
    }

    private static List<Guid> ValidateSkills(IReadOnlyList<Guid>? skillIds, Dictionary<string, string[]> errors)
    {
        var skills = skillIds?.ToList() ?? [];

        if (skills.Count > WorkOrderMessages.MaxSkills)
        {
            errors["skillIds"] = [WorkOrderMessages.SkillsMessage];
        }
        else if (skills.Any(id => id == Guid.Empty) || skills.Distinct().Count() != skills.Count)
        {
            errors["skillIds"] = [WorkOrderMessages.SkillIdsInvalidMessage];
        }

        return skills;
    }

    private static List<string> ValidateTasks(IReadOnlyList<WorkOrderTaskText?>? texts, Dictionary<string, string[]> errors)
    {
        var tasks = new List<string>();
        var items = texts ?? [];

        if (items.Count == 0)
        {
            errors["tasks"] = [WorkOrderMessages.TasksRequiredMessage];

            return tasks;
        }

        if (items.Count > WorkOrderMessages.MaxTasks)
        {
            errors["tasks"] = [WorkOrderMessages.TasksTooManyMessage];

            return tasks;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var label = (items[index]?.Label ?? string.Empty).Trim();

            if (label.Length == 0)
            {
                errors[$"tasks[{index}].label"] = [WorkOrderMessages.TaskRequiredMessage];
            }
            else if (label.Length > TaskMaxLength)
            {
                errors[$"tasks[{index}].label"] = [WorkOrderMessages.TaskTooLongMessage];
            }
            else
            {
                tasks.Add(label);
            }
        }

        return tasks;
    }

    private static List<WorkOrderMaterialInput> ValidateMaterials(
        IReadOnlyList<WorkOrderMaterialText?>? texts, Dictionary<string, string[]> errors)
    {
        var materials = new List<WorkOrderMaterialInput>();
        var items = texts ?? [];

        if (items.Count > WorkOrderMessages.MaxMaterials)
        {
            errors["materials"] = [WorkOrderMessages.MaterialsTooManyMessage];

            return materials;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var prefix = $"materials[{index}]";
            var valid = true;
            var description = (item?.Description ?? string.Empty).Trim();
            var unit = (item?.Unit ?? string.Empty).Trim();

            if (description.Length == 0)
            {
                errors[$"{prefix}.description"] = [WorkOrderMessages.MaterialRequiredMessage];
                valid = false;
            }
            else if (description.Length > MaterialMaxLength)
            {
                errors[$"{prefix}.description"] = [WorkOrderMessages.MaterialTooLongMessage];
                valid = false;
            }

            if (item?.Quantity is not { } quantity
                || quantity <= 0
                || quantity > MaxQuantity
                || Math.Round(quantity, 3) != quantity)
            {
                errors[$"{prefix}.quantity"] = [WorkOrderMessages.QuantityMessage];
                valid = false;
            }

            if (unit.Length is 0 or > UnitMaxLength)
            {
                errors[$"{prefix}.unit"] = [WorkOrderMessages.UnitMessage];
                valid = false;
            }

            if (item?.Source is null || !WorkOrderCodes.Sources.Contains(item.Source))
            {
                errors[$"{prefix}.source"] = [WorkOrderMessages.SourceMessage];
                valid = false;
            }

            if (valid)
            {
                materials.Add(new WorkOrderMaterialInput(
                    item!.QuoteLineId, item.CatalogItemId, description, item.Quantity!.Value, unit, item.Source!));
            }
        }

        return materials;
    }

    private static (DateOnly? Date, string Window) ValidateSchedule(
        string? preferredDate, string? arrivalWindow, Dictionary<string, string[]> errors)
    {
        var window = string.IsNullOrWhiteSpace(arrivalWindow) ? WorkOrderWindow.Any : arrivalWindow;
        DateOnly? date = null;

        if (!WorkOrderWindow.IsValid(window))
        {
            errors["arrivalWindow"] = [WorkOrderMessages.WindowMessage];
        }

        if (!string.IsNullOrWhiteSpace(preferredDate))
        {
            if (DateOnly.TryParseExact(preferredDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                date = parsed;
            }
            else
            {
                errors["preferredDate"] = [WorkOrderMessages.PreferredDateMessage];
            }
        }
        else if (window != WorkOrderWindow.Any && !errors.ContainsKey("arrivalWindow"))
        {
            errors["arrivalWindow"] = [WorkOrderMessages.WindowNeedsDateMessage];
        }

        return (date, window);
    }

    /// <summary>Job type and recurrence consistency (BR-13): a one-time job has none, a recurring one has both parts.</summary>
    public static (string? Frequency, short? Count) ValidateRecurrence(
        string? jobType, WorkOrderRecurrenceText? recurrence, Dictionary<string, string[]> errors)
    {
        if (jobType == "one_time")
        {
            if (recurrence is not null)
            {
                errors["recurrence"] = [WorkOrderMessages.RecurrenceNotAllowedMessage];
            }

            return (null, null);
        }

        if (jobType != "recurring")
        {
            return (null, null);
        }

        if (recurrence?.Frequency is null || !WorkOrderCodes.Frequencies.Contains(recurrence.Frequency))
        {
            errors["recurrence"] = [WorkOrderMessages.RecurrenceFrequencyMessage];

            return (null, null);
        }

        if (recurrence.Count is null or < 2 or > 24)
        {
            errors["recurrence"] = [WorkOrderMessages.RecurrenceCountMessage];

            return (null, null);
        }

        return (recurrence.Frequency, (short)recurrence.Count.Value);
    }
}
