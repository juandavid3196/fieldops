using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.Customers;

internal static class CustomerRecordChecks
{
    /// <summary>
    /// The shared order of the list endpoints: the customer must be visible (404) before the query is parsed (400).
    /// Module authorization already happened in the Api layer.
    /// </summary>
    public static async Task<(CustomerResult<T>? Failure, BranchScope Scope, CustomerRecordQuery? Query)> PrepareAsync<T>(
        ICustomerDetailStore detailStore,
        IBranchScopeResolver scopeResolver,
        CustomerRecordKind kind,
        Guid organizationId,
        Guid membershipId,
        Guid customerId,
        CustomerRecordQueryText text,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (!await detailStore.IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return (CustomerResult<T>.NotFound(), scope, null);
        }

        var query = CustomerRecordRules.Parse(kind, text, out var errors);

        return query is null ? (CustomerResult<T>.Invalid(errors), scope, null) : (null, scope, query);
    }
}

/// <summary>GET /customers/{id}/requests (FR-02, BR-04, BR-05).</summary>
public sealed class ListCustomerRequestsHandler(
    ICustomerStore customerStore,
    ICustomerDetailStore detailStore,
    ICustomerRecordStore recordStore,
    IBranchScopeResolver scopeResolver)
{
    public async Task<CustomerResult<CustomerRequestsPage>> HandleAsync(
        Guid organizationId, Guid membershipId, Guid customerId, CustomerRecordQueryText text, CancellationToken cancellationToken)
    {
        var (failure, scope, query) = await CustomerRecordChecks.PrepareAsync<CustomerRequestsPage>(
            detailStore, scopeResolver, CustomerRecordKind.Requests, organizationId, membershipId, customerId, text, cancellationToken);

        if (failure is not null)
        {
            return failure;
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);
        var slice = await recordStore.ListRequestsAsync(organizationId, scope, customerId, query!, cancellationToken);

        return CustomerResult<CustomerRequestsPage>.Ok(new CustomerRequestsPage(
            slice.Items, slice.TotalCount, slice.CustomerTotal, query!.Page, query.PageSize, context.Timezone));
    }
}

/// <summary>GET /customers/{id}/quotes (FR-03, BR-04, BR-06).</summary>
public sealed class ListCustomerQuotesHandler(
    ICustomerStore customerStore,
    ICustomerDetailStore detailStore,
    ICustomerRecordStore recordStore,
    IBranchScopeResolver scopeResolver,
    TimeProvider timeProvider)
{
    public async Task<CustomerResult<CustomerQuotesPage>> HandleAsync(
        Guid organizationId, Guid membershipId, Guid customerId, CustomerRecordQueryText text, CancellationToken cancellationToken)
    {
        var (failure, scope, query) = await CustomerRecordChecks.PrepareAsync<CustomerQuotesPage>(
            detailStore, scopeResolver, CustomerRecordKind.Quotes, organizationId, membershipId, customerId, text, cancellationToken);

        if (failure is not null)
        {
            return failure;
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);
        var slice = await recordStore.ListQuotesAsync(
            organizationId, scope, customerId, query!, timeProvider.GetUtcNow(), cancellationToken);

        return CustomerResult<CustomerQuotesPage>.Ok(new CustomerQuotesPage(
            slice.Items, slice.TotalCount, slice.CustomerTotal, query!.Page, query.PageSize, context.Currency, context.Timezone));
    }
}

/// <summary>GET /customers/{id}/jobs (FR-04, BR-04, BR-07).</summary>
public sealed class ListCustomerJobsHandler(
    ICustomerStore customerStore,
    ICustomerDetailStore detailStore,
    ICustomerRecordStore recordStore,
    IBranchScopeResolver scopeResolver,
    TimeProvider timeProvider)
{
    public async Task<CustomerResult<CustomerJobsPage>> HandleAsync(
        Guid organizationId, Guid membershipId, Guid customerId, CustomerRecordQueryText text, CancellationToken cancellationToken)
    {
        var (failure, scope, query) = await CustomerRecordChecks.PrepareAsync<CustomerJobsPage>(
            detailStore, scopeResolver, CustomerRecordKind.Jobs, organizationId, membershipId, customerId, text, cancellationToken);

        if (failure is not null)
        {
            return failure;
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);
        var slice = await recordStore.ListJobsAsync(
            organizationId, scope, customerId, query!, timeProvider.GetUtcNow(), cancellationToken);

        return CustomerResult<CustomerJobsPage>.Ok(new CustomerJobsPage(
            slice.Items, slice.TotalCount, slice.CustomerTotal, query!.Page, query.PageSize, context.Timezone));
    }
}

/// <summary>GET /customers/{id}/invoices (FR-05, BR-04, BR-08).</summary>
public sealed class ListCustomerInvoicesHandler(
    ICustomerStore customerStore,
    ICustomerDetailStore detailStore,
    ICustomerRecordStore recordStore,
    IBranchScopeResolver scopeResolver,
    TimeProvider timeProvider)
{
    public async Task<CustomerResult<CustomerInvoicesPage>> HandleAsync(
        Guid organizationId, Guid membershipId, Guid customerId, CustomerRecordQueryText text, CancellationToken cancellationToken)
    {
        var (failure, scope, query) = await CustomerRecordChecks.PrepareAsync<CustomerInvoicesPage>(
            detailStore, scopeResolver, CustomerRecordKind.Invoices, organizationId, membershipId, customerId, text, cancellationToken);

        if (failure is not null)
        {
            return failure;
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);
        var slice = await recordStore.ListInvoicesAsync(
            organizationId, scope, customerId, query!, timeProvider.GetUtcNow(), cancellationToken);

        return CustomerResult<CustomerInvoicesPage>.Ok(new CustomerInvoicesPage(
            slice.Items,
            slice.Summary,
            slice.TotalCount,
            slice.CustomerTotal,
            query!.Page,
            query.PageSize,
            context.Currency,
            context.Timezone));
    }
}

/// <summary>GET /customers/{id}/activity (FR-07, BR-10 to BR-13). The customer is resolved first, then type, period and cursor.</summary>
public sealed class ListCustomerActivityHandler(
    ICustomerStore customerStore,
    ICustomerDetailStore detailStore,
    ICustomerActivityStore activityStore,
    IBranchScopeResolver scopeResolver,
    TimeProvider timeProvider)
{
    public async Task<CustomerResult<CustomerActivityResponse>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid customerId,
        CustomerRecordAccess access,
        string? type,
        string? period,
        string? before,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (!await detailStore.IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return CustomerResult<CustomerActivityResponse>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!CustomerActivityRules.TryParseTypes(type, access, out var types))
        {
            errors["type"] = [CustomerActivityRules.TypeInvalid];
        }

        if (!CustomerActivityRules.TryParsePeriod(period, timeProvider.GetUtcNow(), out var since))
        {
            errors["period"] = [CustomerActivityRules.PeriodInvalid];
        }

        CustomerActivityCursor? cursor = null;

        if (!string.IsNullOrEmpty(before) && !CustomerActivityRules.TryDecodeCursor(before, out cursor))
        {
            errors["before"] = [CustomerActivityRules.BeforeInvalid];
        }

        if (errors.Count > 0)
        {
            return CustomerResult<CustomerActivityResponse>.Invalid(errors);
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);
        var block = await activityStore.ListAsync(
            organizationId, scope, customerId, access, new CustomerActivityQuery(types, since, cursor), cancellationToken);

        return CustomerResult<CustomerActivityResponse>.Ok(new CustomerActivityResponse(
            block.Items,
            CustomerActivityCatalog.AvailableTypes(access),
            block.Next is { } next ? CustomerActivityRules.EncodeCursor(next) : null,
            context.Currency,
            context.Timezone));
    }
}
