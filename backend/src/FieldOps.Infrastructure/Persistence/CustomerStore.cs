using System.Net;
using System.Text.Json;
using FieldOps.Application.Auditing;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Customers;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class CustomerStore(FieldOpsDbContext dbContext) : ICustomerStore
{
    private const int LeadCode = 0;

    private const int ActiveCode = 1;

    private const int ArchivedCode = 2;

    private const string PrimaryPropertyName = "Primary property";

    public async Task<CustomerOrganizationContext?> GetOrganizationContextAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new { organization.Currency, organization.Timezone, organization.CountryCode })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new CustomerOrganizationContext(row.Currency, row.Timezone, row.CountryCode);
    }

    public async Task<IReadOnlyList<CustomerBranchOption>> ListBranchOptionsAsync(
        Guid organizationId, BranchScope scope, CancellationToken cancellationToken)
    {
        var query = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            query = query.Where(branch => ids.Contains(branch.Id));
        }

        return await query
            .OrderBy(branch => branch.Name)
            .ThenBy(branch => branch.Id)
            .Select(branch => new CustomerBranchOption(branch.Id, branch.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> IsBranchAllowedAsync(
        Guid organizationId, BranchScope scope, Guid branchId, bool requireActive, CancellationToken cancellationToken)
    {
        var query = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.Id == branchId);

        if (requireActive)
        {
            query = query.Where(branch => branch.IsActive);
        }

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            query = query.Where(branch => ids.Contains(branch.Id));
        }

        return query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, Guid>> FindActiveBranchesByCodeAsync(
        Guid organizationId,
        BranchScope scope,
        IReadOnlyCollection<string> lowerCodes,
        CancellationToken cancellationToken)
    {
        var codes = lowerCodes.ToArray();
        var query = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId
                && branch.IsActive
                && codes.Contains(branch.Code.ToLower()));

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            query = query.Where(branch => ids.Contains(branch.Id));
        }

        var rows = await query.Select(branch => new { branch.Code, branch.Id }).ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Code.ToLowerInvariant(), row => row.Id, StringComparer.Ordinal);
    }

    public Task<int> CountTagsAsync(
        Guid organizationId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        var ids = tagIds.ToArray();

        return dbContext.CustomerTags.AsNoTracking()
            .CountAsync(tag => tag.OrganizationId == organizationId && ids.Contains(tag.Id), cancellationToken);
    }

    public async Task<CustomerListData> ListAsync(
        Guid organizationId,
        BranchScope scope,
        CustomerListFilter filter,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = Project(FilterCustomers(WhereInScope(organizationId, scope), organizationId, filter), now);

        rows = filter.Balance switch
        {
            CustomerBalanceFilter.None => rows.Where(row => row.Balance == 0m),
            CustomerBalanceFilter.HasBalance => rows.Where(row => row.Balance > 0m),
            CustomerBalanceFilter.Overdue => rows.Where(row => row.HasOverdue),
            _ => rows,
        };

        // Counts honor scope, branch, search and filters but not the tab.
        var groups = await rows
            .GroupBy(row => row.Lifecycle)
            .Select(group => new { Lifecycle = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int Count(int code) => groups.Where(group => group.Lifecycle == code).Sum(group => group.Count);

        var counts = new CustomerTabCounts(
            Count(LeadCode) + Count(ActiveCode), Count(LeadCode), Count(ActiveCode), Count(ArchivedCode));

        var totalCount = filter.Tab switch
        {
            CustomerTab.Leads => counts.Leads,
            CustomerTab.Active => counts.Active,
            CustomerTab.Archived => counts.Archived,
            _ => counts.All,
        };

        rows = filter.Tab switch
        {
            CustomerTab.Leads => rows.Where(row => row.Lifecycle == LeadCode),
            CustomerTab.Active => rows.Where(row => row.Lifecycle == ActiveCode),
            CustomerTab.Archived => rows.Where(row => row.Lifecycle == ArchivedCode),
            _ => rows.Where(row => row.Lifecycle != ArchivedCode),
        };

        var page = await Order(rows, filter.Sort)
            .Skip((filter.Page - 1) * CustomerQueryParser.PageSize)
            .Take(CustomerQueryParser.PageSize)
            .ToListAsync(cancellationToken);

        var items = new List<CustomerListItem>(page.Count);

        foreach (var row in page)
        {
            CustomerLastService? last = null;

            if (row.LastAt is { } lastAt)
            {
                last = new CustomerLastService(lastAt, await LastSummaryAsync(row.Id, lastAt, cancellationToken));
            }

            items.Add(new CustomerListItem(
                row.Id,
                CustomerStatus.TypeText(row.Type),
                row.DisplayName,
                row.PrimaryEmail,
                row.PrimaryPhone,
                row.PropertyCount,
                last,
                row.NextAt is { } nextAt ? new CustomerNextService(nextAt) : null,
                row.Balance,
                CustomerStatus.Lifecycle(row.IsActive, row.HasDoneWork),
                CustomerStatus.Display(row.IsActive, row.HasDoneWork, row.HasOverdue),
                row.BranchId));
        }

        return new CustomerListData(items, counts, totalCount);
    }

    public async Task<CustomerMetricsData> GetMetricsAsync(
        Guid organizationId,
        BranchScope scope,
        Guid? branchId,
        DateTimeOffset monthStartUtc,
        DateTimeOffset nextMonthStartUtc,
        CancellationToken cancellationToken)
    {
        var customers = WhereInScope(organizationId, scope);

        if (branchId is { } branch)
        {
            customers = customers.Where(customer => customer.BranchId == branch);
        }

        var total = await customers.CountAsync(customer => customer.IsActive, cancellationToken);

        var active = await Project(customers, DateTimeOffset.MinValue)
            .CountAsync(row => row.Lifecycle == ActiveCode, cancellationToken);

        var newThisMonth = await customers.CountAsync(
            customer => customer.CreatedAt >= monthStartUtc && customer.CreatedAt < nextMonthStartUtc,
            cancellationToken);

        var outstanding = await dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.OrganizationId == organizationId
                && (invoice.Status == InvoiceStatus.Sent
                    || invoice.Status == InvoiceStatus.PartiallyPaid
                    || invoice.Status == InvoiceStatus.Overdue)
                && customers.Any(customer => customer.Id == invoice.CustomerId))
            .SumAsync(invoice => invoice.BalanceDue, cancellationToken);

        return new CustomerMetricsData(total, active, newThisMonth, outstanding);
    }

    public async Task<CustomerDetail?> GetDetailAsync(
        Guid organizationId, BranchScope scope, Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await WhereInScope(organizationId, scope)
            .AsNoTracking()
            .Where(candidate => candidate.Id == customerId)
            .SingleOrDefaultAsync(cancellationToken);

        return customer is null ? null : await BuildDetailAsync(customer, cancellationToken);
    }

    public async Task<CustomerType?> GetTypeAsync(
        Guid organizationId, BranchScope scope, Guid customerId, CancellationToken cancellationToken)
    {
        var types = await WhereInScope(organizationId, scope)
            .Where(customer => customer.Id == customerId)
            .Select(customer => (CustomerType?)customer.Type)
            .ToListAsync(cancellationToken);

        return types.SingleOrDefault();
    }

    // One SaveChangesAsync: customer, contact, property, assignments and audit row are written together or not at all.
    public async Task<Guid> CreateAsync(
        Guid organizationId,
        CustomerWrite write,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var customerId = AddCustomer(organizationId, write.Values, write.BranchId, write.CountryCode, write.TagIds);

        var (_, after) = AuditFieldDiff.ForCreate(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = CustomerStatus.TypeText(write.Values.Type),
            ["branchId"] = write.BranchId,
            ["tagCount"] = write.TagIds.Count,
        });

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            CustomerAuditActions.Created,
            CustomerAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: customerId,
            branchId: write.BranchId,
            ipAddress: clientIp,
            afterData: after));

        await dbContext.SaveChangesAsync(cancellationToken);

        return customerId;
    }

    public async Task<CustomerDetail?> UpdateAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerValues values,
        Guid branchId,
        IReadOnlyList<Guid> tagIds,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var customer = await WhereInScope(organizationId, scope)
            .Where(candidate => candidate.Id == customerId)
            .SingleOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return null;
        }

        var contact = await FirstContactAsync(organizationId, customerId, track: true, cancellationToken);
        var property = await FirstPropertyAsync(organizationId, customerId, track: true, cancellationToken);
        var assignments = await dbContext.CustomerTagAssignments
            .Where(assignment => assignment.OrganizationId == organizationId && assignment.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var changed = new List<string>();

        void Diff(string name, object? before, object? after)
        {
            if (!Equals(before, after))
            {
                changed.Add(name);
            }
        }

        Diff("type", customer.Type, values.Type);
        Diff(
            "companyName",
            customer.Type == CustomerType.Company ? customer.DisplayName : null,
            values.Type == CustomerType.Company ? values.CompanyName : null);
        Diff("firstName", contact?.FirstName, values.FirstName);
        Diff("lastName", contact?.LastName, values.LastName);
        Diff("title", contact?.Title, values.Title);
        Diff("email", contact?.Email, values.Email);
        Diff("phone", contact?.Phone, values.Phone);
        Diff("prefersEmail", contact?.PrefersEmail, values.PrefersEmail);
        Diff("prefersSms", contact?.PrefersSms, values.PrefersSms);
        Diff("addressLine1", property?.AddressLine1, values.AddressLine1);
        Diff("city", property?.City, values.City);
        Diff("stateRegion", property?.StateRegion, values.StateRegion);
        Diff("postalCode", property?.PostalCode, values.PostalCode);
        Diff("serviceInstructions", property?.ServiceNotes, values.ServiceInstructions);
        Diff("internalNote", customer.Notes, values.InternalNote);
        Diff("branchId", customer.BranchId, branchId);

        var currentTags = assignments.Select(assignment => assignment.TagId).ToHashSet();
        var tagsChanged = !currentTags.SetEquals(tagIds);

        if (tagsChanged)
        {
            changed.Add("tags");
        }

        if (changed.Count > 0)
        {
            var customerChanged = customer.Update(
                branchId,
                values.Type,
                values.DisplayName,
                values.Email,
                values.Phone,
                values.InternalNote,
                now);

            if (contact is null)
            {
                dbContext.CustomerContacts.Add(CustomerContact.CreatePrimary(
                    organizationId,
                    customerId,
                    values.FirstName,
                    values.LastName,
                    values.Email,
                    values.Phone,
                    values.Title,
                    values.PrefersEmail,
                    values.PrefersSms));
            }
            else
            {
                contact.Update(
                    values.FirstName,
                    values.LastName,
                    values.Email,
                    values.Phone,
                    values.Title,
                    values.PrefersEmail,
                    values.PrefersSms,
                    now);
            }

            if (property is null)
            {
                dbContext.Properties.Add(Property.Create(
                    organizationId,
                    customerId,
                    PrimaryPropertyName,
                    values.AddressLine1,
                    values.City,
                    await CountryCodeAsync(organizationId, cancellationToken),
                    branchId,
                    values.StateRegion,
                    values.PostalCode,
                    values.ServiceInstructions));
            }
            else
            {
                property.Update(
                    branchId,
                    values.AddressLine1,
                    values.City,
                    values.StateRegion,
                    values.PostalCode,
                    values.ServiceInstructions,
                    now);
            }

            if (tagsChanged)
            {
                foreach (var assignment in assignments.Where(assignment => !tagIds.Contains(assignment.TagId)))
                {
                    dbContext.CustomerTagAssignments.Remove(assignment);
                }

                foreach (var tagId in tagIds.Where(tagId => !currentTags.Contains(tagId)))
                {
                    dbContext.CustomerTagAssignments.Add(
                        CustomerTagAssignment.Create(organizationId, customerId, tagId));
                }
            }

            if (!customerChanged)
            {
                customer.Touch(now);
            }

            // Field names only: values (email, phone, addresses, notes) never reach the audit row.
            dbContext.AuditLogs.Add(AuditLog.Create(
                organizationId,
                CustomerAuditActions.Updated,
                CustomerAuditActions.EntityType,
                actorUserId: actorUserId,
                entityId: customerId,
                branchId: customer.BranchId,
                ipAddress: clientIp,
                metadata: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["changedFields"] = changed,
                })));

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await BuildDetailAsync(customer, cancellationToken);
    }

    public async Task<CustomerStateOutcome> SetActiveAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        bool isActive,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var customer = await WhereInScope(organizationId, scope)
            .Where(candidate => candidate.Id == customerId)
            .SingleOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return CustomerStateOutcome.NotFound;
        }

        if (!customer.SetActive(isActive, now))
        {
            return CustomerStateOutcome.NoChange;
        }

        var (before, after) = AuditFieldDiff.ForStateChange(!isActive, isActive);

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            isActive ? CustomerAuditActions.Reactivated : CustomerAuditActions.Archived,
            CustomerAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: customerId,
            branchId: customer.BranchId,
            ipAddress: clientIp,
            beforeData: before,
            afterData: after));

        await dbContext.SaveChangesAsync(cancellationToken);

        return CustomerStateOutcome.Changed;
    }

    public async Task<IReadOnlyList<CustomerDuplicateRow>> FindDuplicatesAsync(
        Guid organizationId,
        string? email,
        string? phone,
        Guid? excludeCustomerId,
        CancellationToken cancellationToken)
    {
        // Organization-wide on purpose: archived customers and other branches are matches too (BR-14).
        var customers = dbContext.Customers.AsNoTracking()
            .Where(customer => customer.OrganizationId == organizationId
                && (excludeCustomerId == null || customer.Id != excludeCustomerId)
                && dbContext.CustomerContacts.Any(contact =>
                    contact.CustomerId == customer.Id
                    && contact.OrganizationId == organizationId
                    && ((email != null && contact.Email == email) || (phone != null && contact.Phone == phone))));

        var rows = await Project(customers, DateTimeOffset.MinValue)
            .OrderBy(row => row.DisplayName)
            .ThenBy(row => row.Id)
            .Take(3)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.Id).ToArray();

        var matched = await dbContext.CustomerContacts.AsNoTracking()
            .Where(contact => contact.OrganizationId == organizationId
                && ids.Contains(contact.CustomerId)
                && ((email != null && contact.Email == email) || (phone != null && contact.Phone == phone)))
            .Select(contact => new
            {
                contact.CustomerId,
                EmailMatched = email != null && contact.Email == email,
                PhoneMatched = phone != null && contact.Phone == phone,
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new CustomerDuplicateRow(
            row.Id,
            row.DisplayName,
            row.PrimaryEmail,
            row.PrimaryPhone,
            row.PropertyCount,
            row.BranchId,
            CustomerStatus.Display(row.IsActive, row.HasDoneWork, row.HasOverdue),
            matched.Any(match => match.CustomerId == row.Id && match.EmailMatched),
            matched.Any(match => match.CustomerId == row.Id && match.PhoneMatched)))];
    }

    public async Task<IReadOnlyList<CustomerContactMatch>> FindContactMatchesAsync(
        Guid organizationId,
        IReadOnlyCollection<string> emails,
        IReadOnlyCollection<string> phones,
        CancellationToken cancellationToken)
    {
        var emailList = emails.ToArray();
        var phoneList = phones.ToArray();

        return await (
            from contact in dbContext.CustomerContacts.AsNoTracking()
            join customer in dbContext.Customers.AsNoTracking() on contact.CustomerId equals customer.Id
            where customer.OrganizationId == organizationId
                && contact.OrganizationId == organizationId
                && ((contact.Email != null && emailList.Contains(contact.Email))
                    || (contact.Phone != null && phoneList.Contains(contact.Phone)))
            orderby customer.DisplayName, customer.Id
            select new CustomerContactMatch(contact.Email, contact.Phone, customer.DisplayName))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerTagView>> ListTagsAsync(
        Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.CustomerTags.AsNoTracking()
            .Where(tag => tag.OrganizationId == organizationId)
            .OrderBy(tag => tag.NormalizedName)
            .ThenBy(tag => tag.Id)
            .Select(tag => new CustomerTagView(tag.Id, tag.Name))
            .ToListAsync(cancellationToken);

    public async Task<(CustomerTagView Tag, bool Created)> GetOrCreateTagAsync(
        Guid organizationId, string name, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        var normalized = CustomerTag.NormalizeName(name);

        var existing = await FindTagAsync(organizationId, normalized, cancellationToken);

        if (existing is not null)
        {
            return (existing, false);
        }

        var tag = CustomerTag.Create(organizationId, name);
        var (_, after) = AuditFieldDiff.ForCreate(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = tag.Name,
        });

        dbContext.CustomerTags.Add(tag);
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            CustomerAuditActions.TagCreated,
            CustomerAuditActions.TagEntityType,
            actorUserId: actorUserId,
            entityId: tag.Id,
            ipAddress: clientIp,
            afterData: after));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            return (new CustomerTagView(tag.Id, tag.Name), true);
        }
        catch (DbUpdateException ex) when (IsTagNameViolation(ex))
        {
            // A concurrent request created the same name first: return that tag, write no audit row.
            dbContext.ChangeTracker.Clear();

            var winner = await FindTagAsync(organizationId, normalized, cancellationToken);

            return winner is null ? throw new InvalidOperationException("The tag name conflict could not be resolved.", ex) : (winner, false);
        }
    }

    // One SaveChangesAsync (implicit single transaction): every customer, any missing tag and the single imported audit row.
    public async Task<int> ImportAsync(
        Guid organizationId,
        IReadOnlyList<CustomerImportRow> rows,
        string countryCode,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var normalizedNames = rows
                .SelectMany(row => row.TagNames)
                .Select(CustomerTag.NormalizeName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var tagIds = normalizedNames.Length == 0
                ? new Dictionary<string, Guid>(StringComparer.Ordinal)
                : await dbContext.CustomerTags.AsNoTracking()
                    .Where(tag => tag.OrganizationId == organizationId && normalizedNames.Contains(tag.NormalizedName))
                    .ToDictionaryAsync(tag => tag.NormalizedName, tag => tag.Id, StringComparer.Ordinal, cancellationToken);

            foreach (var name in rows.SelectMany(row => row.TagNames))
            {
                var normalized = CustomerTag.NormalizeName(name);

                if (!tagIds.ContainsKey(normalized))
                {
                    var tag = CustomerTag.Create(organizationId, name);
                    dbContext.CustomerTags.Add(tag);
                    tagIds[normalized] = tag.Id;
                }
            }

            foreach (var row in rows)
            {
                AddCustomer(
                    organizationId,
                    row.Values,
                    row.BranchId,
                    countryCode,
                    [.. row.TagNames.Select(name => tagIds[CustomerTag.NormalizeName(name)]).Distinct()]);
            }

            dbContext.AuditLogs.Add(AuditLog.Create(
                organizationId,
                CustomerAuditActions.Imported,
                CustomerAuditActions.EntityType,
                actorUserId: actorUserId,
                ipAddress: clientIp,
                metadata: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["importedCount"] = rows.Count,
                })));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                return rows.Count;
            }
            catch (DbUpdateException ex) when (attempt == 0 && IsTagNameViolation(ex))
            {
                // A concurrent tag create won the unique name: start over once from a clean tracker so the
                // winner's tag is resolved instead of created.
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private Guid AddCustomer(
        Guid organizationId, CustomerValues values, Guid branchId, string countryCode, IReadOnlyList<Guid> tagIds)
    {
        var customer = Customer.Create(
            organizationId,
            branchId,
            values.Type,
            values.DisplayName,
            values.Email,
            values.Phone,
            values.InternalNote);

        dbContext.Customers.Add(customer);
        dbContext.CustomerContacts.Add(CustomerContact.CreatePrimary(
            organizationId,
            customer.Id,
            values.FirstName,
            values.LastName,
            values.Email,
            values.Phone,
            values.Title,
            values.PrefersEmail,
            values.PrefersSms));
        dbContext.Properties.Add(Property.Create(
            organizationId,
            customer.Id,
            PrimaryPropertyName,
            values.AddressLine1,
            values.City,
            countryCode,
            branchId,
            values.StateRegion,
            values.PostalCode,
            values.ServiceInstructions));

        foreach (var tagId in tagIds)
        {
            dbContext.CustomerTagAssignments.Add(CustomerTagAssignment.Create(organizationId, customer.Id, tagId));
        }

        return customer.Id;
    }

    private async Task<string> CountryCodeAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var code = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.CountryCode)
            .SingleOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(code) ? "US" : code.Trim().ToUpperInvariant();
    }

    private Task<CustomerTagView?> FindTagAsync(
        Guid organizationId, string normalizedName, CancellationToken cancellationToken) =>
        dbContext.CustomerTags.AsNoTracking()
            .Where(tag => tag.OrganizationId == organizationId && tag.NormalizedName == normalizedName)
            .Select(tag => new CustomerTagView(tag.Id, tag.Name))
            .SingleOrDefaultAsync(cancellationToken);

    private Task<CustomerContact?> FirstContactAsync(
        Guid organizationId, Guid customerId, bool track, CancellationToken cancellationToken)
    {
        var query = dbContext.CustomerContacts.Where(contact =>
            contact.OrganizationId == organizationId
            && contact.CustomerId == customerId
            && contact.IsPrimary
            && contact.IsActive);

        return (track ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    // The first property is the oldest active one (BR-11).
    private Task<Property?> FirstPropertyAsync(
        Guid organizationId, Guid customerId, bool track, CancellationToken cancellationToken)
    {
        var query = dbContext.Properties
            .Where(property => property.OrganizationId == organizationId
                && property.CustomerId == customerId
                && property.IsActive)
            .OrderBy(property => property.CreatedAt)
            .ThenBy(property => property.Id);

        return (track ? query : query.AsNoTracking()).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CustomerDetail> BuildDetailAsync(Customer customer, CancellationToken cancellationToken)
    {
        var contact = await FirstContactAsync(customer.OrganizationId, customer.Id, track: false, cancellationToken);
        var property = await FirstPropertyAsync(customer.OrganizationId, customer.Id, track: false, cancellationToken);

        var tags = await (
            from assignment in dbContext.CustomerTagAssignments.AsNoTracking()
            join tag in dbContext.CustomerTags.AsNoTracking() on assignment.TagId equals tag.Id
            where assignment.OrganizationId == customer.OrganizationId && assignment.CustomerId == customer.Id
            orderby tag.NormalizedName, tag.Id
            select new CustomerTagView(tag.Id, tag.Name))
            .ToListAsync(cancellationToken);

        var status = await Project(
                dbContext.Customers.AsNoTracking().Where(candidate => candidate.Id == customer.Id), DateTimeOffset.MinValue)
            .Select(row => new { row.HasDoneWork, row.HasOverdue })
            .SingleAsync(cancellationToken);

        return new CustomerDetail(
            customer.Id,
            CustomerStatus.TypeText(customer.Type),
            customer.Type == CustomerType.Company ? customer.DisplayName : null,
            new CustomerDetailContact(
                contact?.FirstName ?? string.Empty,
                contact?.LastName ?? string.Empty,
                contact?.Title,
                contact?.Email ?? string.Empty,
                contact?.Phone,
                contact?.PrefersEmail ?? true,
                contact?.PrefersSms ?? false),
            new CustomerDetailProperty(
                property?.AddressLine1 ?? string.Empty,
                property?.City ?? string.Empty,
                property?.StateRegion,
                property?.PostalCode),
            property?.ServiceNotes,
            customer.Notes,
            customer.BranchId,
            tags,
            CustomerStatus.Lifecycle(customer.IsActive, status.HasDoneWork),
            CustomerStatus.Display(customer.IsActive, status.HasDoneWork, status.HasOverdue),
            customer.IsActive);
    }

    // The first line of the work order scope of the latest completed visit (AS-01).
    private async Task<string?> LastSummaryAsync(Guid customerId, DateTimeOffset lastAt, CancellationToken cancellationToken)
    {
        var snapshot = await (
            from visit in dbContext.Visits.AsNoTracking()
            join order in dbContext.WorkOrders.AsNoTracking() on visit.WorkOrderId equals order.Id
            where order.CustomerId == customerId
                && (visit.Status == VisitStatus.Completed || visit.Status == VisitStatus.Approved)
                && (visit.ActualCompletedAt ?? visit.ScheduledEnd) == lastAt
            orderby visit.Id
            select order.ScopeSnapshot)
            .FirstOrDefaultAsync(cancellationToken);

        var line = snapshot?.Split('\n')[0].Trim();

        return string.IsNullOrEmpty(line) ? null : line;
    }

    // Every query filters the organization first and then the branch scope.
    private IQueryable<Customer> WhereInScope(Guid organizationId, BranchScope scope)
    {
        var query = dbContext.Customers.Where(customer => customer.OrganizationId == organizationId);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            query = query.Where(customer => ids.Contains(customer.BranchId));
        }

        return query;
    }

    private IQueryable<Customer> FilterCustomers(IQueryable<Customer> query, Guid organizationId, CustomerListFilter filter)
    {
        query = query.AsNoTracking();

        if (filter.Type is { } type)
        {
            query = query.Where(customer => customer.Type == type);
        }

        if (filter.BranchId is { } branchId)
        {
            query = query.Where(customer => customer.BranchId == branchId);
        }

        if (filter.TagIds.Count > 0)
        {
            var tagIds = filter.TagIds.ToArray();
            query = query.Where(customer => dbContext.CustomerTagAssignments.Any(assignment =>
                assignment.OrganizationId == organizationId
                && assignment.CustomerId == customer.Id
                && tagIds.Contains(assignment.TagId)));
        }

        if (filter.Search is { } search)
        {
            var pattern = $"%{search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
            var digits = CustomerNormalizer.DigitsOnly(search);
            var matchPhone = digits.Length >= 3;

            query = query.Where(customer =>
                EF.Functions.ILike(customer.DisplayName, pattern)
                || dbContext.CustomerContacts.Any(contact =>
                    contact.CustomerId == customer.Id
                    && contact.IsPrimary
                    && contact.IsActive
                    && (EF.Functions.ILike(contact.FirstName, pattern)
                        || (contact.LastName != null && EF.Functions.ILike(contact.LastName, pattern))
                        || (contact.Email != null && EF.Functions.ILike(contact.Email, pattern))
                        || (matchPhone && contact.Phone != null && contact.Phone.Contains(digits)))));
        }

        return query;
    }

    // Lifecycle is derived (BR-03): 0 lead, 1 active, 2 archived. The completed-work, balance and activity
    // values are correlated subqueries, so list, tab counts, filters and sorts share one definition.
    private IQueryable<Row> Project(IQueryable<Customer> query, DateTimeOffset now) =>
        from customer in query
        let hasDoneWork =
            dbContext.Visits.Any(visit =>
                (visit.Status == VisitStatus.Completed || visit.Status == VisitStatus.Approved)
                && dbContext.WorkOrders.Any(order => order.Id == visit.WorkOrderId && order.CustomerId == customer.Id))
            || dbContext.WorkOrders.Any(order =>
                order.CustomerId == customer.Id
                && (order.Status == WorkOrderStatus.Completed || order.Status == WorkOrderStatus.ApprovedForBilling))
        let balance = dbContext.Invoices
            .Where(invoice => invoice.CustomerId == customer.Id
                && (invoice.Status == InvoiceStatus.Sent
                    || invoice.Status == InvoiceStatus.PartiallyPaid
                    || invoice.Status == InvoiceStatus.Overdue))
            .Sum(invoice => invoice.BalanceDue)
        let hasOverdue = dbContext.Invoices.Any(invoice =>
            invoice.CustomerId == customer.Id && invoice.Status == InvoiceStatus.Overdue)
        let lastAt = (
            from visit in dbContext.Visits
            join order in dbContext.WorkOrders on visit.WorkOrderId equals order.Id
            where order.CustomerId == customer.Id
                && (visit.Status == VisitStatus.Completed || visit.Status == VisitStatus.Approved)
            select visit.ActualCompletedAt ?? visit.ScheduledEnd).Max()
        let nextAt = (
            from visit in dbContext.Visits
            join order in dbContext.WorkOrders on visit.WorkOrderId equals order.Id
            where order.CustomerId == customer.Id
                && (visit.Status == VisitStatus.Scheduled
                    || visit.Status == VisitStatus.Assigned
                    || visit.Status == VisitStatus.OnTheWay)
                && visit.ScheduledStart >= now
            select visit.ScheduledStart).Min()
        select new Row
        {
            Id = customer.Id,
            Type = customer.Type,
            DisplayName = customer.DisplayName,
            PrimaryEmail = customer.PrimaryEmail,
            PrimaryPhone = customer.PrimaryPhone,
            BranchId = customer.BranchId,
            IsActive = customer.IsActive,
            CreatedAt = customer.CreatedAt,
            UpdatedAt = customer.UpdatedAt,
            PropertyCount = dbContext.Properties.Count(property => property.CustomerId == customer.Id && property.IsActive),
            HasDoneWork = hasDoneWork,
            Balance = balance,
            HasOverdue = hasOverdue,
            LastAt = lastAt,
            NextAt = nextAt,
            Lifecycle = !customer.IsActive ? ArchivedCode : hasDoneWork ? ActiveCode : LeadCode,
        };

    // Every sort ends with the id so pages are stable.
    private static IOrderedQueryable<Row> Order(IQueryable<Row> rows, CustomerSort sort) =>
        sort switch
        {
            CustomerSort.NameAsc => rows.OrderBy(row => row.DisplayName.ToLower()).ThenBy(row => row.Id),
            CustomerSort.NameDesc => rows.OrderByDescending(row => row.DisplayName.ToLower()).ThenBy(row => row.Id),
            CustomerSort.Newest => rows.OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id),
            CustomerSort.BalanceDesc => rows.OrderByDescending(row => row.Balance).ThenBy(row => row.Id),
            _ => rows
                .OrderBy(row => row.LastAt == null ? 1 : 0)
                .ThenByDescending(row => row.LastAt)
                .ThenByDescending(row => row.UpdatedAt)
                .ThenBy(row => row.Id),
        };

    private static bool IsTagNameViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == CustomerTagConfiguration.OrgNormalizedNameIndexName;

    private sealed class Row
    {
        public Guid Id { get; init; }

        public CustomerType Type { get; init; }

        public string DisplayName { get; init; } = string.Empty;

        public string? PrimaryEmail { get; init; }

        public string? PrimaryPhone { get; init; }

        public Guid BranchId { get; init; }

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public int PropertyCount { get; init; }

        public bool HasDoneWork { get; init; }

        public decimal Balance { get; init; }

        public bool HasOverdue { get; init; }

        public DateTimeOffset? LastAt { get; init; }

        public DateTimeOffset? NextAt { get; init; }

        public int Lifecycle { get; init; }
    }
}
