using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Quotes;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class WorkOrderStore
{
    /// <summary>The approval guards of the editor, draft and create (BR-05): the conflict code, or null when the quote qualifies.</summary>
    private static string? GuardQuote(Quote quote)
    {
        if (quote.Status != QuoteStatus.Approved || quote.ApprovedVersionId is null)
        {
            return WorkOrderMessages.QuoteNotApprovedCode;
        }

        return quote.CustomerId is null || quote.PropertyId is null ? WorkOrderMessages.CustomerRequiredCode : null;
    }

    /// <summary>The editor of a visible quote (BR-05, BR-06, BR-07); writes nothing.</summary>
    private async Task<WorkOrderOutcome<WorkOrderEditor>> BuildEditorAsync(
        Guid organizationId, BranchScope scope, Quote quote, CancellationToken cancellationToken)
    {
        if (GuardQuote(quote) is { } code)
        {
            return new WorkOrderOutcome<WorkOrderEditor>.Conflict(code);
        }

        var versionId = quote.ApprovedVersionId!.Value;
        var existing = await OrdersOfQuote(organizationId, quote.Id).AsNoTracking()
            .OrderBy(order => order.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null && existing.QuoteVersionId != versionId)
        {
            return new WorkOrderOutcome<WorkOrderEditor>.Conflict(WorkOrderMessages.QuoteNotApprovedCode);
        }

        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var zone = OrganizationTime.FindZone(organization.Timezone);
        var request = await dbContext.ServiceRequests.AsNoTracking()
            .SingleAsync(candidate => candidate.OrganizationId == organizationId && candidate.Id == quote.RequestId, cancellationToken);
        var version = await dbContext.QuoteVersions.AsNoTracking()
            .SingleAsync(candidate => candidate.OrganizationId == organizationId && candidate.Id == versionId, cancellationToken);
        var approval = await ReadApprovalAsync(organizationId, versionId, cancellationToken);

        var customer = await dbContext.Customers.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == quote.CustomerId)
            .Select(candidate => new { candidate.DisplayName, candidate.BranchId })
            .SingleAsync(cancellationToken);
        var contact = await dbContext.CustomerContacts.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId
                && candidate.CustomerId == quote.CustomerId
                && candidate.IsPrimary
                && candidate.IsActive)
            .OrderBy(candidate => candidate.Id)
            .Select(candidate => new { candidate.Phone, candidate.Email })
            .FirstOrDefaultAsync(cancellationToken);
        var property = await dbContext.Properties.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == quote.PropertyId)
            .Select(candidate => new
            {
                candidate.AddressLine1,
                candidate.AddressLine2,
                candidate.City,
                candidate.StateRegion,
                candidate.PostalCode,
                candidate.AccessInstructions,
            })
            .SingleAsync(cancellationToken);

        var assessment = await CompletedAssessmentReader.ReadAsync(dbContext, organizationId, request.Id, zone, cancellationToken);
        var options = await ReadOptionsAsync(organizationId, scope, organization.Timezone, cancellationToken);

        WorkOrderValues values;
        EditorWorkOrder? workOrder = null;

        if (existing is null)
        {
            var branchId = new[] { quote.BranchId, request.BranchId, customer.BranchId }
                .FirstOrDefault(candidate => candidate is { } id && options.Branches.Any(branch => branch.Id == id));

            values = await PrefillAsync(organizationId, version, request, branchId, options, cancellationToken);
        }
        else
        {
            var branchZone = OrganizationTime.FindZone(
                await dbContext.Branches.AsNoTracking()
                    .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == existing.BranchId)
                    .Select(candidate => candidate.Timezone)
                    .SingleAsync(cancellationToken)
                ?? organization.Timezone);
            var skillIds = await dbContext.WorkOrderRequiredSkills.AsNoTracking()
                .Where(link => link.WorkOrderId == existing.Id)
                .Select(link => link.SkillId)
                .ToListAsync(cancellationToken);
            var stored = await ReadStoredChildrenAsync(organizationId, existing.Id, cancellationToken);
            var (date, window) = ReadSchedule(existing, branchZone);

            values = new WorkOrderValues(
                existing.Title,
                existing.JobType,
                existing.ServiceCategoryId,
                existing.BranchId,
                WorkOrderCodes.PriorityCode(existing.Priority),
                existing.EstimatedDurationMinutes,
                skillIds,
                stored.Tasks,
                stored.Materials,
                existing.InternalInstructions,
                date,
                window,
                Recurrence(existing),
                Communication(existing));
            workOrder = new EditorWorkOrder(
                existing.Id,
                RequestCardRules.DisplayNumber(organization.WorkOrderPrefix, existing.WorkOrderNumber),
                WorkOrderCodes.StatusCode(existing.Status),
                existing.UpdatedAt);
        }

        return new WorkOrderOutcome<WorkOrderEditor>.Succeeded(new WorkOrderEditor(
            new EditorQuote(
                quote.Id,
                RequestCardRules.DisplayNumber(organization.QuotePrefix, quote.QuoteNumber),
                OrganizationTime.ToZone(approval.RespondedAt, zone),
                approval.Total,
                version.Currency),
            request.Id,
            new EditorCustomer(
                customer.DisplayName,
                NullIfBlank(contact?.Phone),
                NullIfBlank(contact?.Email),
                QuoteStore.FormatAddress(
                    property.AddressLine1, property.AddressLine2, property.City, property.StateRegion, property.PostalCode) ?? string.Empty),
            NullIfBlank(property.AccessInstructions),
            assessment is null
                ? null
                : new EditorAssessment(
                    assessment.Id,
                    NullIfBlank(assessment.Technician?.Name),
                    NullIfBlank(assessment.Diagnosis),
                    assessment.Photos.Select(photo => new EditorPhoto(photo.Id)).ToList()),
            workOrder,
            values,
            options,
            organization.Timezone));
    }

    // Active branches in the caller scope, active categories and active skills (the editor options, BR-03, BR-09).
    private async Task<EditorOptions> ReadOptionsAsync(
        Guid organizationId, BranchScope scope, string organizationTimezone, CancellationToken cancellationToken)
    {
        var branchQuery = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            branchQuery = branchQuery.Where(branch => ids.Contains(branch.Id));
        }

        var branches = await branchQuery
            .OrderBy(branch => branch.Name)
            .ThenBy(branch => branch.Id)
            .Select(branch => new { branch.Id, branch.Name, branch.Timezone })
            .ToListAsync(cancellationToken);
        var categories = await dbContext.ServiceCategories.AsNoTracking()
            .Where(category => category.OrganizationId == organizationId && category.IsActive)
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new EditorOption(category.Id, category.Name))
            .ToListAsync(cancellationToken);
        var skills = await dbContext.Skills.AsNoTracking()
            .Where(skill => skill.OrganizationId == organizationId && skill.IsActive)
            .OrderBy(skill => skill.Name)
            .ThenBy(skill => skill.Id)
            .Select(skill => new EditorOption(skill.Id, skill.Name))
            .ToListAsync(cancellationToken);

        return new EditorOptions(
            branches.Select(branch => new EditorBranch(branch.Id, branch.Name, branch.Timezone ?? organizationTimezone)).ToList(),
            categories,
            skills);
    }

    // BR-07: nothing is saved; the approved lines supply the tasks (services) and the planned materials (products).
    private async Task<WorkOrderValues> PrefillAsync(
        Guid organizationId,
        QuoteVersion version,
        FieldOps.Domain.Requests.ServiceRequest request,
        Guid? branchId,
        EditorOptions options,
        CancellationToken cancellationToken)
    {
        var lines = await ApprovedLinesAsync(organizationId, version.Id, cancellationToken);
        var categoryId = request.CategoryId is { } candidate && options.Categories.Any(category => category.Id == candidate)
            ? candidate
            : (Guid?)null;

        return new WorkOrderValues(
            Truncate(version.Scope, WorkOrderValidator.TitleMaxLength),
            "one_time",
            categoryId,
            branchId,
            WorkOrderCodes.PriorityFromUrgency(request.Urgency),
            null,
            [],
            lines.Where(line => line.LineType == CatalogItemType.Service)
                .Select(line => new WorkOrderTaskValue(Truncate(line.Name, WorkOrderValidator.TaskMaxLength)))
                .ToList(),
            lines.Where(line => line.LineType == CatalogItemType.Product)
                .Select(line => new WorkOrderMaterialValue(
                    line.Id,
                    null,
                    Truncate(line.Name, WorkOrderValidator.MaterialMaxLength),
                    line.Quantity,
                    Truncate(line.Unit, WorkOrderValidator.UnitMaxLength),
                    "truck_stock"))
                .ToList(),
            null,
            null,
            WorkOrderWindow.Any,
            null,
            new WorkOrderCommunication(true, true, true));
    }
}
