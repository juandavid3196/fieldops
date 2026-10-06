using System.Text.Json;
using FieldOps.Application.Features.ChecklistTemplates;
using FieldOps.Application.Features.Quotes;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>Checklist template persistence: organization-scoped list and create (create-work-order BR-11, BR-19).</summary>
internal sealed class ChecklistTemplateStore(FieldOpsDbContext dbContext) : IChecklistTemplateStore
{
    private const string NameIndex = "ux_checklist_templates_org_name";

    private const string UniqueViolation = "23505";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ChecklistTemplateView>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var templates = await dbContext.ChecklistTemplates.AsNoTracking()
            .Where(template => template.OrganizationId == organizationId && template.IsActive)
            .OrderBy(template => template.Name.ToLower())
            .ThenBy(template => template.Id)
            .Select(template => new { template.Id, template.Name, template.ServiceCategoryId })
            .ToListAsync(cancellationToken);
        var ids = templates.Select(template => template.Id).ToArray();
        var items = await dbContext.ChecklistTemplateItems.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && ids.Contains(item.TemplateId))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item => new { item.TemplateId, item.Label })
            .ToListAsync(cancellationToken);

        return templates
            .Select(template => new ChecklistTemplateView(
                template.Id,
                template.Name,
                template.ServiceCategoryId,
                items.Where(item => item.TemplateId == template.Id).Select(item => new ChecklistItemView(item.Label)).ToList()))
            .ToList();
    }

    public async Task<ChecklistTemplateOutcome> CreateAsync(
        QuoteActor actor, ChecklistTemplateInput input, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (input.ServiceCategoryId is { } categoryId
            && !await dbContext.ServiceCategories.AsNoTracking().AnyAsync(
                candidate => candidate.OrganizationId == organizationId && candidate.Id == categoryId && candidate.IsActive,
                cancellationToken))
        {
            errors["serviceCategoryId"] = [ChecklistTemplateMessages.CategoryMessage];
        }

        var lowered = input.Name.ToLowerInvariant();

        if (await dbContext.ChecklistTemplates.AsNoTracking().AnyAsync(
                candidate => candidate.OrganizationId == organizationId && candidate.Name.ToLower() == lowered,
                cancellationToken))
        {
            errors["name"] = [ChecklistTemplateMessages.NameDuplicate];
        }

        if (errors.Count > 0)
        {
            return new ChecklistTemplateOutcome.Invalid(errors);
        }

        var template = ChecklistTemplate.Create(organizationId, input.ServiceCategoryId, input.Name, actor.UserId);
        var items = input.Items
            .Select((label, index) => ChecklistTemplateItem.Create(organizationId, template.Id, label, index))
            .ToList();

        dbContext.ChecklistTemplates.Add(template);
        dbContext.ChecklistTemplateItems.AddRange(items);

        // BR-19: the name and the category plus the item count; never the item labels.
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            "checklist_template.created",
            "checklist_template",
            actor.UserId,
            template.Id,
            null,
            actor.IpAddress,
            null,
            JsonSerializer.Serialize(new { name = template.Name, serviceCategoryId = template.ServiceCategoryId }, JsonOptions),
            JsonSerializer.Serialize(new { itemCount = items.Count }, JsonOptions)));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation, ConstraintName: NameIndex })
        {
            // A concurrent save with the same name won the case-insensitive unique index.
            dbContext.ChangeTracker.Clear();

            return new ChecklistTemplateOutcome.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["name"] = [ChecklistTemplateMessages.NameDuplicate],
            });
        }

        return new ChecklistTemplateOutcome.Created(new ChecklistTemplateView(
            template.Id,
            template.Name,
            template.ServiceCategoryId,
            items.Select(item => new ChecklistItemView(item.Label)).ToList()));
    }
}
