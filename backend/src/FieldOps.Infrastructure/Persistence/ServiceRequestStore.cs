using System.Linq.Expressions;
using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Requests pipeline persistence. Reads are no-tracking and always filter by organization first, then
/// by branch scope (BR-02). Mutations serialize on the request row (see <c>ServiceRequestStore.Mutations</c>).
/// </summary>
internal sealed partial class ServiceRequestStore(FieldOpsDbContext dbContext, TimeProvider timeProvider) : IServiceRequestStore
{
    private const string AuditEntityType = "service_request";

    private const int CustomerOptionLimit = 20;

    private static readonly string[] ManagerRoles = ["owner", "dispatcher"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Expression<Func<ServiceRequest, bool>> IsOpenRequest = request =>
        request.Status == RequestStatus.New
        || request.Status == RequestStatus.NeedsReview
        || request.Status == RequestStatus.AssessmentScheduled
        || request.Status == RequestStatus.ReadyForQuote;

    public async Task<RequestOrganizationContext?> GetOrganizationContextAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new
            {
                organization.Name,
                organization.Phone,
                organization.Timezone,
                organization.RequestPrefix,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new RequestOrganizationContext(row.Name, row.Phone, row.Timezone, row.RequestPrefix);
    }

    public async Task<IReadOnlyDictionary<string, string[]>> ValidateFilterIdsAsync(
        Guid organizationId, PipelineFilter filter, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (filter.AssigneeUserId is { } assigneeId
            && !await IsManagerMemberAsync(organizationId, assigneeId, cancellationToken))
        {
            errors["assigneeUserId"] = [ServiceRequestMessages.FilterInvalid];
        }

        if (filter.CategoryId is { } categoryId
            && !await dbContext.ServiceCategories.AsNoTracking().AnyAsync(
                category => category.Id == categoryId && category.OrganizationId == organizationId,
                cancellationToken))
        {
            errors["categoryId"] = [ServiceRequestMessages.FilterInvalid];
        }

        return errors;
    }

    public async Task<RequestOptions> GetOptionsAsync(
        Guid organizationId, BranchScope scope, CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The organization is no longer available.");

        var categoryRows = await dbContext.ServiceCategories.AsNoTracking()
            .Where(category => category.OrganizationId == organizationId && category.IsActive)
            .Select(category => new { category.Id, category.Name })
            .ToListAsync(cancellationToken);

        var serviceRows = await dbContext.CatalogItems.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && item.IsActive
                && item.Type == CatalogItemType.Service
                && item.CategoryId != null)
            .Select(item => new { item.Id, item.Name, CategoryId = item.CategoryId!.Value })
            .ToListAsync(cancellationToken);

        var categories = categoryRows
            .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .Select(category => new OptionCategory(
                category.Id,
                category.Name,
                serviceRows
                    .Where(service => service.CategoryId == category.Id)
                    .OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(service => new OptionService(service.Id, service.Name))
                    .ToList()))
            .ToList();

        var branchQuery = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive);
        var technicianQuery = dbContext.TechnicianProfiles.AsNoTracking()
            .Where(technician => technician.OrganizationId == organizationId
                && technician.Status == TechnicianStatus.Active);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            branchQuery = branchQuery.Where(branch => ids.Contains(branch.Id));
            technicianQuery = technicianQuery.Where(technician => ids.Contains(technician.BranchId));
        }

        var branches = await branchQuery
            .OrderBy(branch => branch.Name)
            .ThenBy(branch => branch.Id)
            .Select(branch => new OptionBranch(branch.Id, branch.Name))
            .ToListAsync(cancellationToken);

        var technicianRows = await technicianQuery
            .OrderBy(technician => technician.FirstName)
            .ThenBy(technician => technician.LastName)
            .ThenBy(technician => technician.Id)
            .Select(technician => new { technician.Id, technician.FirstName, technician.LastName, technician.BranchId })
            .ToListAsync(cancellationToken);

        var technicians = technicianRows
            .Select(row =>
            {
                var name = FullName(row.FirstName, row.LastName);

                return new OptionTechnician(row.Id, name, RequestCardRules.Initials(name), row.BranchId);
            })
            .ToList();

        return new RequestOptions(
            organization.RequestPrefix,
            organization.Timezone,
            categories,
            await ListAssigneesAsync(organizationId, cancellationToken),
            branches,
            technicians);
    }

    public async Task<CustomerOptions> GetCustomerOptionsAsync(
        Guid organizationId, BranchScope scope, string? search, CancellationToken cancellationToken)
    {
        var query = dbContext.Customers.AsNoTracking()
            .Where(customer => customer.OrganizationId == organizationId && customer.IsActive);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            query = query.Where(customer => ids.Contains(customer.BranchId));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = RequestSearch.ContainsPattern(search);
            query = query.Where(customer =>
                EF.Functions.ILike(customer.DisplayName, pattern, RequestSearch.EscapeCharacter.ToString()));
        }

        var customers = await query
            .OrderBy(customer => customer.DisplayName)
            .ThenBy(customer => customer.Id)
            .Take(CustomerOptionLimit)
            .Select(customer => new { customer.Id, customer.DisplayName, customer.BranchId })
            .ToListAsync(cancellationToken);

        var customerIds = customers.Select(customer => customer.Id).ToArray();

        var contacts = await dbContext.CustomerContacts.AsNoTracking()
            .Where(contact => contact.OrganizationId == organizationId
                && contact.IsActive
                && customerIds.Contains(contact.CustomerId))
            .OrderByDescending(contact => contact.IsPrimary)
            .ThenBy(contact => contact.FirstName)
            .ThenBy(contact => contact.Id)
            .Select(contact => new
            {
                contact.Id,
                contact.CustomerId,
                contact.FirstName,
                contact.LastName,
                contact.Email,
                contact.Phone,
                contact.IsPrimary,
            })
            .ToListAsync(cancellationToken);

        var properties = await dbContext.Properties.AsNoTracking()
            .Where(property => property.OrganizationId == organizationId
                && property.IsActive
                && customerIds.Contains(property.CustomerId))
            .OrderByDescending(property => property.IsPrimary)
            .ThenBy(property => property.Name)
            .ThenBy(property => property.Id)
            .Select(property => new
            {
                property.Id,
                property.CustomerId,
                property.Name,
                property.AddressLine1,
                property.City,
                property.IsPrimary,
            })
            .ToListAsync(cancellationToken);

        return new CustomerOptions(customers
            .Select(customer => new CustomerOption(
                customer.Id,
                customer.DisplayName,
                customer.BranchId,
                contacts
                    .Where(contact => contact.CustomerId == customer.Id)
                    .Select(contact => new CustomerOptionContact(
                        contact.Id,
                        FullName(contact.FirstName, contact.LastName),
                        contact.Email,
                        contact.Phone,
                        contact.IsPrimary))
                    .ToList(),
                properties
                    .Where(property => property.CustomerId == customer.Id)
                    .Select(property => new CustomerOptionProperty(
                        property.Id, property.Name, property.AddressLine1, property.City, property.IsPrimary))
                    .ToList()))
            .ToList());
    }

    private async Task<IReadOnlyList<OptionAssignee>> ListAssigneesAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var members = await dbContext.OrganizationUsers.AsNoTracking()
            .Where(member => member.OrganizationId == organizationId && member.Status == UserStatus.Active)
            .Join(
                dbContext.Roles.Where(role => ManagerRoles.Contains(role.Code)),
                member => member.RoleId,
                role => role.Id,
                (member, role) => new { member.Id, member.UserId, member.IsAllBranches, role.Code })
            .ToListAsync(cancellationToken);

        var userIds = members.Select(member => member.UserId).ToArray();
        var memberIds = members.Select(member => member.Id).ToArray();

        var names = await dbContext.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FirstName, user.LastName })
            .ToListAsync(cancellationToken);

        var links = await dbContext.OrganizationUserBranches.AsNoTracking()
            .Where(link => memberIds.Contains(link.OrganizationUserId))
            .Select(link => new { link.OrganizationUserId, link.BranchId })
            .ToListAsync(cancellationToken);

        return members
            .Select(member =>
            {
                var user = names.Single(candidate => candidate.Id == member.UserId);
                var name = FullName(user.FirstName, user.LastName);
                object branchIds = member.IsAllBranches || member.Code == "owner"
                    ? "all"
                    : links.Where(link => link.OrganizationUserId == member.Id).Select(link => link.BranchId).ToArray();

                return new OptionAssignee(member.UserId, name, RequestCardRules.Initials(name), branchIds);
            })
            .OrderBy(assignee => assignee.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(assignee => assignee.UserId)
            .ToList();
    }

    /// <summary>An active member of the organization with an owner or dispatcher role (BR-09).</summary>
    private Task<bool> IsManagerMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.OrganizationUsers.AsNoTracking()
            .Where(member => member.OrganizationId == organizationId
                && member.UserId == userId
                && member.Status == UserStatus.Active)
            .Join(
                dbContext.Roles.Where(role => ManagerRoles.Contains(role.Code)),
                member => member.RoleId,
                role => role.Id,
                (member, role) => member.Id)
            .AnyAsync(cancellationToken);

    private IQueryable<ServiceRequest> Visible(Guid organizationId, BranchScope scope)
    {
        var query = dbContext.ServiceRequests.AsNoTracking()
            .Where(request => request.OrganizationId == organizationId);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            query = query.Where(request => request.BranchId == null || ids.Contains(request.BranchId.Value));
        }

        return query;
    }

    private static string FullName(string? first, string? last) =>
        string.Join(' ', new[] { first, last }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

    private static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
}
