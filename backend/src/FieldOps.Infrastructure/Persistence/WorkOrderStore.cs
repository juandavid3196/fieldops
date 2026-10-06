using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Work order creation persistence. Reads are no-tracking and always filter by organization first, then by the
/// branch scope (quotes: the request branch, create-work-order BR-02; orders: their own branch, BR-03).
/// Mutations serialize on the request row, then the quote row (see <c>WorkOrderStore.Mutations</c>).
/// </summary>
internal sealed partial class WorkOrderStore(FieldOpsDbContext dbContext, TimeProvider timeProvider) : IWorkOrderStore
{
    private const string AuditEntityType = "work_order";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WorkOrderLookup> LookupAsync(
        Guid organizationId, BranchScope scope, Guid quoteId, CancellationToken cancellationToken)
    {
        var quote = await VisibleQuotes(organizationId, scope)
            .Where(candidate => candidate.Id == quoteId)
            .Select(candidate => new { candidate.ApprovedVersionId })
            .SingleOrDefaultAsync(cancellationToken);

        if (quote is null)
        {
            return new WorkOrderLookup(false, null);
        }

        if (quote.ApprovedVersionId is not { } versionId)
        {
            return new WorkOrderLookup(true, null);
        }

        var order = await dbContext.WorkOrders.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId
                && candidate.QuoteVersionId == versionId
                && candidate.Status != WorkOrderStatus.Draft)
            .Select(candidate => new { candidate.Id, candidate.WorkOrderNumber })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return new WorkOrderLookup(true, null);
        }

        var prefix = await ReadWorkOrderPrefixAsync(organizationId, cancellationToken);

        return new WorkOrderLookup(true, new WorkOrderRef(order.Id, RequestCardRules.DisplayNumber(prefix, order.WorkOrderNumber)));
    }

    public async Task<WorkOrderOutcome<WorkOrderEditor>> GetEditorAsync(
        Guid organizationId, BranchScope scope, Guid quoteId, CancellationToken cancellationToken)
    {
        var quote = await VisibleQuotes(organizationId, scope)
            .Where(candidate => candidate.Id == quoteId)
            .SingleOrDefaultAsync(cancellationToken);

        return quote is null
            ? new WorkOrderOutcome<WorkOrderEditor>.NotFound()
            : await BuildEditorAsync(organizationId, scope, quote, cancellationToken);
    }

    public async Task<WorkOrderPage> ListAsync(
        Guid organizationId, BranchScope scope, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = VisibleOrders(organizationId, scope);
        var total = await query.CountAsync(cancellationToken);
        var prefix = await ReadWorkOrderPrefixAsync(organizationId, cancellationToken);

        var rows = await query
            .OrderByDescending(order => order.CreatedAt)
            .ThenBy(order => order.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(order => new
            {
                order.Id,
                order.WorkOrderNumber,
                order.Title,
                order.Priority,
                order.Status,
                order.CreatedAt,
                CustomerName = dbContext.Customers
                    .Where(customer => customer.OrganizationId == organizationId && customer.Id == order.CustomerId)
                    .Select(customer => customer.DisplayName)
                    .FirstOrDefault(),
                BranchName = dbContext.Branches
                    .Where(branch => branch.OrganizationId == organizationId && branch.Id == order.BranchId)
                    .Select(branch => branch.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new WorkOrderPage(
            rows.Select(row => new WorkOrderListItem(
                    row.Id,
                    RequestCardRules.DisplayNumber(prefix, row.WorkOrderNumber),
                    row.Title,
                    row.CustomerName ?? string.Empty,
                    row.BranchName ?? string.Empty,
                    WorkOrderCodes.PriorityCode(row.Priority),
                    WorkOrderCodes.StatusCode(row.Status),
                    row.CreatedAt))
                .ToList(),
            total);
    }

    public async Task<WorkOrderDetail?> GetAsync(
        Guid organizationId, BranchScope scope, Guid workOrderId, bool canManage, CancellationToken cancellationToken)
    {
        var order = await VisibleOrders(organizationId, scope)
            .Where(candidate => candidate.Id == workOrderId)
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var version = await dbContext.QuoteVersions.AsNoTracking()
            .SingleAsync(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.QuoteVersionId, cancellationToken);
        var quote = await dbContext.Quotes.AsNoTracking()
            .SingleAsync(candidate => candidate.OrganizationId == organizationId && candidate.Id == version.QuoteId, cancellationToken);
        var approval = await ReadApprovalAsync(organizationId, version.Id, cancellationToken);
        var customerName = await dbContext.Customers.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.CustomerId)
            .Select(candidate => candidate.DisplayName)
            .SingleAsync(cancellationToken);
        var property = await dbContext.Properties.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.PropertyId)
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
        var category = await dbContext.ServiceCategories.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.ServiceCategoryId)
            .Select(candidate => new EditorOption(candidate.Id, candidate.Name))
            .SingleAsync(cancellationToken);
        var branch = await dbContext.Branches.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.BranchId)
            .Select(candidate => new { candidate.Id, candidate.Name, candidate.Timezone })
            .SingleAsync(cancellationToken);
        var branchTimezone = branch.Timezone ?? organization.Timezone;
        var stored = await ReadStoredChildrenAsync(organizationId, order.Id, cancellationToken);

        var skills = await dbContext.WorkOrderRequiredSkills.AsNoTracking()
            .Where(link => link.WorkOrderId == order.Id)
            .Join(
                dbContext.Skills.AsNoTracking().Where(skill => skill.OrganizationId == organizationId),
                link => link.SkillId,
                skill => skill.Id,
                (link, skill) => new { skill.Id, skill.Name })
            .OrderBy(skill => skill.Name)
            .ToListAsync(cancellationToken);
        var skillOptions = skills.Select(skill => new EditorOption(skill.Id, skill.Name)).ToList();

        var visits = await dbContext.Visits.AsNoTracking()
            .Where(visit => visit.OrganizationId == organizationId && visit.WorkOrderId == order.Id)
            .OrderBy(visit => visit.VisitNumber)
            .Select(visit => new { visit.VisitNumber, visit.Status })
            .ToListAsync(cancellationToken);

        var (date, window) = ReadSchedule(order, OrganizationTime.FindZone(branchTimezone));

        return new WorkOrderDetail(
            order.Id,
            RequestCardRules.DisplayNumber(organization.WorkOrderPrefix, order.WorkOrderNumber),
            WorkOrderCodes.StatusCode(order.Status),
            order.UpdatedAt,
            canManage,
            new JobQuote(
                quote.Id,
                RequestCardRules.DisplayNumber(organization.QuotePrefix, quote.QuoteNumber),
                approval.Total,
                version.Currency),
            new JobCustomer(order.CustomerId, customerName),
            QuoteStore.FormatAddress(property.AddressLine1, property.AddressLine2, property.City, property.StateRegion, property.PostalCode),
            NullIfBlank(property.AccessInstructions),
            order.JobType,
            category,
            new JobBranch(branch.Id, branch.Name, branchTimezone),
            WorkOrderCodes.PriorityCode(order.Priority),
            order.EstimatedDurationMinutes,
            skillOptions,
            Recurrence(order),
            order.PreferredStart,
            order.PreferredEnd,
            window,
            date,
            stored.Tasks,
            stored.Materials,
            order.InternalInstructions,
            Communication(order),
            visits.Select(visit => new JobVisit(visit.VisitNumber, WorkOrderCodes.VisitStatusCode(visit.Status))).ToList());
    }

    /// <summary>Quotes of the organization whose request is visible: the branch is the request branch or null (quote-builder BR-07).</summary>
    private IQueryable<Quote> VisibleQuotes(Guid organizationId, BranchScope scope)
    {
        var query = dbContext.Quotes.AsNoTracking().Where(quote => quote.OrganizationId == organizationId);

        if (scope.All)
        {
            return query;
        }

        var ids = scope.BranchIds.ToArray();

        return query.Where(quote => dbContext.ServiceRequests.Any(request =>
            request.Id == quote.RequestId
            && request.OrganizationId == organizationId
            && (request.BranchId == null || ids.Contains(request.BranchId.Value))));
    }

    /// <summary>Work orders of the organization whose own branch is in the scope (BR-03).</summary>
    private IQueryable<WorkOrder> VisibleOrders(Guid organizationId, BranchScope scope)
    {
        var query = dbContext.WorkOrders.AsNoTracking().Where(order => order.OrganizationId == organizationId);

        if (scope.All)
        {
            return query;
        }

        var ids = scope.BranchIds.ToArray();

        return query.Where(order => ids.Contains(order.BranchId));
    }

    private IQueryable<WorkOrder> OrdersOfQuote(Guid organizationId, Guid quoteId) =>
        dbContext.WorkOrders.Where(order => order.OrganizationId == organizationId
            && dbContext.QuoteVersions.Any(version => version.OrganizationId == organizationId
                && version.QuoteId == quoteId
                && version.Id == order.QuoteVersionId));

    /// <summary>
    /// The lines of the approved version that become tasks and materials: non-optional lines plus the optional
    /// lines selected in the approval (BR-07, BR-09). One definition for the prefill and the validation.
    /// </summary>
    private Task<List<QuoteLine>> ApprovedLinesAsync(Guid organizationId, Guid versionId, CancellationToken cancellationToken) =>
        dbContext.QuoteLines.AsNoTracking()
            .Where(line => line.OrganizationId == organizationId
                && line.QuoteVersionId == versionId
                && (!line.IsOptional
                    || dbContext.QuoteResponseOptionalLines.Any(selected => selected.OrganizationId == organizationId
                        && selected.QuoteVersionId == versionId
                        && selected.QuoteLineId == line.Id)))
            .OrderBy(line => line.SortOrder)
            .ThenBy(line => line.Id)
            .ToListAsync(cancellationToken);

    private async Task<Approval> ReadApprovalAsync(Guid organizationId, Guid versionId, CancellationToken cancellationToken)
    {
        var response = await dbContext.QuoteResponses.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId
                && candidate.QuoteVersionId == versionId
                && candidate.Response == QuoteStatus.Approved)
            .OrderByDescending(candidate => candidate.RespondedAt)
            .Select(candidate => new { candidate.RespondedAt, candidate.Total })
            .FirstOrDefaultAsync(cancellationToken);

        if (response is not null)
        {
            return new Approval(response.RespondedAt, response.Total ?? 0m);
        }

        var version = await dbContext.QuoteVersions.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == versionId)
            .Select(candidate => new { candidate.SentAt, candidate.CreatedAt, candidate.Total })
            .SingleAsync(cancellationToken);

        return new Approval(version.SentAt ?? version.CreatedAt, version.Total);
    }

    private async Task<StoredChildren> ReadStoredChildrenAsync(Guid organizationId, Guid workOrderId, CancellationToken cancellationToken)
    {
        var tasks = await dbContext.WorkOrderChecklistTemplates.AsNoTracking()
            .Where(task => task.WorkOrderId == workOrderId)
            .OrderBy(task => task.SortOrder)
            .ThenBy(task => task.Id)
            .Select(task => new WorkOrderTaskValue(task.Label))
            .ToListAsync(cancellationToken);

        var materials = await dbContext.WorkOrderPlannedMaterials.AsNoTracking()
            .Where(material => material.OrganizationId == organizationId && material.WorkOrderId == workOrderId)
            .OrderBy(material => material.SortOrder)
            .ThenBy(material => material.Id)
            .Select(material => new WorkOrderMaterialValue(
                material.QuoteLineId,
                material.CatalogItemId,
                material.Description,
                material.Quantity,
                material.Unit,
                material.Source))
            .ToListAsync(cancellationToken);

        return new StoredChildren(tasks, materials);
    }

    private async Task<OrganizationInfo> ReadOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new OrganizationInfo(
                organization.Timezone,
                organization.QuotePrefix,
                organization.RequestPrefix,
                organization.WorkOrderPrefix))
            .SingleAsync(cancellationToken);

    private Task<string> ReadWorkOrderPrefixAsync(Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.WorkOrderPrefix)
            .SingleAsync(cancellationToken);

    private static (DateOnly? Date, string Window) ReadSchedule(WorkOrder order, TimeZoneInfo zone)
    {
        if (order.PreferredStart is not { } start || order.PreferredEnd is not { } end)
        {
            return (null, WorkOrderWindow.Any);
        }

        var (date, window) = WorkOrderWindow.Read(start, end, zone);

        return (date, window);
    }

    private static WorkOrderRecurrence? Recurrence(WorkOrder order) =>
        order.RecurrenceFrequency is { } frequency && order.RecurrenceCount is { } count
            ? new WorkOrderRecurrence(frequency, count)
            : null;

    private static WorkOrderCommunication Communication(WorkOrder order) =>
        new(order.NotifyCustomerWhenScheduled, order.SendTechnicianDetails, order.SendArrivalReminder);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int length)
    {
        var trimmed = value.Trim();

        return trimmed.Length <= length ? trimmed : trimmed[..length].TrimEnd();
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private sealed record OrganizationInfo(string Timezone, string QuotePrefix, string RequestPrefix, string WorkOrderPrefix);

    private sealed record Approval(DateTimeOffset RespondedAt, decimal Total);

    private sealed record StoredChildren(
        IReadOnlyList<WorkOrderTaskValue> Tasks, IReadOnlyList<WorkOrderMaterialValue> Materials);
}
