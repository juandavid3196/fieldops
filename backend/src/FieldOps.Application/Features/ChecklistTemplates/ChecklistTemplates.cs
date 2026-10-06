using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.ChecklistTemplates;

public static class ChecklistTemplateMessages
{
    public const int NameMaxLength = 120;

    public const int MaxItems = 50;

    public const int ItemMaxLength = 240;

    public const string NameRequired = "Enter a template name.";

    public const string NameTooLong = "Template name must be 120 characters or fewer.";

    public const string NameDuplicate = "A template with this name already exists.";

    public const string ItemsMessage = "Add between 1 and 50 tasks of up to 240 characters.";

    public const string CategoryMessage = "Select a valid service category.";
}

public sealed record ChecklistItemText(string? Label);

/// <summary>The raw body of POST /checklist-templates.</summary>
public sealed record ChecklistTemplateText(string? Name, Guid? ServiceCategoryId, IReadOnlyList<ChecklistItemText?>? Items);

public sealed record ChecklistTemplateInput(string Name, Guid? ServiceCategoryId, IReadOnlyList<string> Items);

public sealed record ChecklistItemView(string Label);

public sealed record ChecklistTemplateView(Guid Id, string Name, Guid? ServiceCategoryId, IReadOnlyList<ChecklistItemView> Items);

public abstract record ChecklistTemplateOutcome
{
    private ChecklistTemplateOutcome()
    {
    }

    public sealed record Created(ChecklistTemplateView Template) : ChecklistTemplateOutcome;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : ChecklistTemplateOutcome;
}

/// <summary>Persistence port of the checklist templates; scoped to the session organization.</summary>
public interface IChecklistTemplateStore
{
    Task<IReadOnlyList<ChecklistTemplateView>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<ChecklistTemplateOutcome> CreateAsync(
        QuoteActor actor, ChecklistTemplateInput input, CancellationToken cancellationToken);
}

/// <summary>Shape validation of a template (create-work-order BR-11); all errors at once.</summary>
public static class ChecklistTemplateValidator
{
    public static ChecklistTemplateInput? Validate(ChecklistTemplateText input, Dictionary<string, string[]> errors)
    {
        var before = errors.Count;
        var name = (input.Name ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            errors["name"] = [ChecklistTemplateMessages.NameRequired];
        }
        else if (name.Length > ChecklistTemplateMessages.NameMaxLength)
        {
            errors["name"] = [ChecklistTemplateMessages.NameTooLong];
        }

        var items = (input.Items ?? []).Select(item => (item?.Label ?? string.Empty).Trim()).ToList();

        if (items.Count is < 1 or > ChecklistTemplateMessages.MaxItems
            || items.Any(label => label.Length is 0 or > ChecklistTemplateMessages.ItemMaxLength))
        {
            errors["items"] = [ChecklistTemplateMessages.ItemsMessage];
        }

        if (input.ServiceCategoryId == Guid.Empty)
        {
            errors["serviceCategoryId"] = [ChecklistTemplateMessages.CategoryMessage];
        }

        return errors.Count > before ? null : new ChecklistTemplateInput(name, input.ServiceCategoryId, items);
    }
}

public sealed class ListChecklistTemplatesHandler(IChecklistTemplateStore store)
{
    public Task<IReadOnlyList<ChecklistTemplateView>> HandleAsync(Guid organizationId, CancellationToken cancellationToken) =>
        store.ListAsync(organizationId, cancellationToken);
}

public sealed class CreateChecklistTemplateHandler(IChecklistTemplateStore store)
{
    public async Task<ServiceRequestResult<ChecklistTemplateView>> HandleAsync(
        MembershipCall call, ChecklistTemplateText body, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var input = ChecklistTemplateValidator.Validate(body, errors);

        if (input is null)
        {
            return ServiceRequestResult<ChecklistTemplateView>.Invalid(errors);
        }

        var actor = new QuoteActor(call.OrganizationId, call.UserId, BranchScope.Everything, call.IpAddress);

        return await store.CreateAsync(actor, input, cancellationToken) switch
        {
            ChecklistTemplateOutcome.Created created => ServiceRequestResult<ChecklistTemplateView>.Ok(created.Template),
            ChecklistTemplateOutcome.Invalid invalid => ServiceRequestResult<ChecklistTemplateView>.Invalid(invalid.Errors),
            _ => throw new InvalidOperationException("Unknown checklist template outcome."),
        };
    }
}
