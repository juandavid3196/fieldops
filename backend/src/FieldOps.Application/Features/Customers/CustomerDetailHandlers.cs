using System.Net;
using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.Customers;

internal static class CustomerDetailChecks
{
    /// <summary>Maps a property outcome to its result; <see cref="CustomerPropertyOutcome.Changed"/> is the caller's success.</summary>
    public static CustomerResult<T> Failure<T>(CustomerPropertyOutcome outcome) =>
        outcome switch
        {
            CustomerPropertyOutcome.NotFound => CustomerResult<T>.NotFound(),
            CustomerPropertyOutcome.AlreadyPrimary => CustomerResult<T>.Conflict(CustomerMessages.PropertyAlreadyPrimary),
            CustomerPropertyOutcome.ArchivedCannotBePrimary => CustomerResult<T>.Conflict(CustomerMessages.PropertyArchivedNotPrimary),
            CustomerPropertyOutcome.PrimaryCannotBeArchived => CustomerResult<T>.Conflict(CustomerMessages.PrimaryCannotBeArchived),
            CustomerPropertyOutcome.AlreadyArchived => CustomerResult<T>.Conflict(CustomerMessages.PropertyAlreadyArchived),
            CustomerPropertyOutcome.AlreadyActive => CustomerResult<T>.Conflict(CustomerMessages.PropertyAlreadyActive),
            CustomerPropertyOutcome.ArchivedCannotBeEdited => CustomerResult<T>.Conflict(CustomerMessages.PropertyArchivedNotEditable),
            CustomerPropertyOutcome.PrimaryChangedConcurrently => CustomerResult<T>.Conflict(CustomerMessages.PrimaryChangedConcurrently),
            _ => throw new InvalidOperationException("Unexpected property outcome."),
        };

    public static async Task<CustomerOrganizationContext> ContextAsync(
        ICustomerStore store, Guid organizationId, CancellationToken cancellationToken) =>
        await store.GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The session organization does not exist.");

    /// <summary>
    /// Validates the body and its branch (BR-05, BR-06). The branch must be an active in-scope branch
    /// unless it equals <paramref name="currentBranchId"/> (an unchanged branch on edit).
    /// </summary>
    public static async Task<(CustomerPropertyValues? Values, Dictionary<string, string[]> Errors)> ValidateAsync(
        ICustomerStore store,
        Guid organizationId,
        BranchScope scope,
        string? countryCode,
        CustomerPropertyInput input,
        Guid? currentBranchId,
        CancellationToken cancellationToken)
    {
        var values = CustomerPropertyRules.Validate(input, CustomerRules.IsUnitedStates(countryCode), out var errors);
        Guid? branchId = null;

        if (string.IsNullOrWhiteSpace(input.BranchId))
        {
            errors[CustomerRules.BranchIdKey] = [CustomerMessages.PropertyBranchRequired];
        }
        else if (!CustomerQueryParser.TryParseGuid(input.BranchId, out var parsed))
        {
            errors[CustomerRules.BranchIdKey] = [CustomerMessages.BranchNotAllowed];
        }
        else if (parsed == currentBranchId
            || await store.IsBranchAllowedAsync(organizationId, scope, parsed, requireActive: true, cancellationToken))
        {
            branchId = parsed;
        }
        else
        {
            errors[CustomerRules.BranchIdKey] = [CustomerMessages.BranchNotAllowed];
        }

        return values is null || branchId is null || errors.Count > 0
            ? (null, errors)
            : (values with { BranchId = branchId.Value }, errors);
    }
}

/// <summary>GET /customers/{id}/detail (FR-02, BR-03, BR-11, BR-12). A hidden customer is null.</summary>
public sealed class GetCustomerOverviewHandler(
    ICustomerStore customerStore, ICustomerDetailStore detailStore, IBranchScopeResolver scopeResolver)
{
    public async Task<CustomerOverview?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid customerId, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        var data = await detailStore.GetOverviewAsync(organizationId, scope, customerId, cancellationToken);
        var detail = data is null
            ? null
            : await customerStore.GetDetailAsync(organizationId, scope, customerId, cancellationToken);

        if (data is null || detail is null)
        {
            return null;
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);

        return new CustomerOverview(
            customerId,
            detail.Type,
            data.DisplayName,
            detail.Contact,
            detail.Lifecycle,
            detail.DisplayStatus,
            detail.IsActive,
            data.Balance,
            context.Currency,
            context.Timezone,
            new CustomerDetailSummary(data.TotalJobs, data.LifetimeValue, data.CustomerSince, data.LastServiceAt),
            data.LastInvoice,
            detail.Tags,
            detail.InternalNote);
    }
}

/// <summary>GET /customers/{id}/properties (FR-04, BR-04, BR-13).</summary>
public sealed class ListCustomerPropertiesHandler(
    ICustomerStore customerStore,
    ICustomerDetailStore detailStore,
    IBranchScopeResolver scopeResolver,
    TimeProvider timeProvider)
{
    public async Task<CustomerPropertyList?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid customerId, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        var items = await detailStore.ListPropertiesAsync(
            organizationId, scope, customerId, timeProvider.GetUtcNow(), cancellationToken);

        if (items is null)
        {
            return null;
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);

        return new CustomerPropertyList(items, context.Timezone);
    }
}

/// <summary>GET /customers/{id}/properties/{propertyId} (FR-05). A hidden customer or foreign property is null.</summary>
public sealed class GetCustomerPropertyHandler(
    ICustomerDetailStore detailStore, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<CustomerPropertyView?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid customerId, Guid propertyId, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        return await detailStore.GetPropertyAsync(
            organizationId, scope, customerId, propertyId, timeProvider.GetUtcNow(), cancellationToken);
    }
}

/// <summary>POST /customers/{id}/properties (FR-05, BR-05 to BR-07). The customer is resolved before the body.</summary>
public sealed class CreateCustomerPropertyHandler(
    ICustomerStore customerStore,
    ICustomerDetailStore detailStore,
    IBranchScopeResolver scopeResolver,
    TimeProvider timeProvider)
{
    public async Task<CustomerResult<CustomerPropertyView>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid customerId,
        Guid actorUserId,
        IPAddress? clientIp,
        CustomerPropertyInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (!await detailStore.IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return CustomerResult<CustomerPropertyView>.NotFound();
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);

        var (values, errors) = await CustomerDetailChecks.ValidateAsync(
            customerStore, organizationId, scope, context.CountryCode, input, currentBranchId: null, cancellationToken);

        if (values is null)
        {
            return CustomerResult<CustomerPropertyView>.Invalid(errors);
        }

        var countryCode = string.IsNullOrWhiteSpace(context.CountryCode) ? "US" : context.CountryCode.Trim().ToUpperInvariant();

        var created = await detailStore.CreatePropertyAsync(
            organizationId,
            scope,
            customerId,
            values,
            countryCode,
            actorUserId,
            clientIp,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return created is null
            ? CustomerResult<CustomerPropertyView>.NotFound()
            : CustomerResult<CustomerPropertyView>.Ok(created);
    }
}

/// <summary>PUT /customers/{id}/properties/{propertyId} (FR-05, BR-05 to BR-07).</summary>
public sealed class UpdateCustomerPropertyHandler(
    ICustomerStore customerStore,
    ICustomerDetailStore detailStore,
    IBranchScopeResolver scopeResolver,
    TimeProvider timeProvider)
{
    public async Task<CustomerResult<CustomerPropertyView>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid customerId,
        Guid propertyId,
        Guid actorUserId,
        IPAddress? clientIp,
        CustomerPropertyInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        var state = await detailStore.GetPropertyStateAsync(organizationId, scope, customerId, propertyId, cancellationToken);

        if (state is null)
        {
            return CustomerResult<CustomerPropertyView>.NotFound();
        }

        if (!state.IsActive)
        {
            return CustomerDetailChecks.Failure<CustomerPropertyView>(CustomerPropertyOutcome.ArchivedCannotBeEdited);
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);

        var (values, errors) = await CustomerDetailChecks.ValidateAsync(
            customerStore, organizationId, scope, context.CountryCode, input, state.BranchId, cancellationToken);

        if (values is null)
        {
            return CustomerResult<CustomerPropertyView>.Invalid(errors);
        }

        var change = await detailStore.UpdatePropertyAsync(
            organizationId,
            scope,
            customerId,
            propertyId,
            values,
            actorUserId,
            clientIp,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return change.Outcome == CustomerPropertyOutcome.Changed
            ? CustomerResult<CustomerPropertyView>.Ok(change.Property!)
            : CustomerDetailChecks.Failure<CustomerPropertyView>(change.Outcome);
    }
}

/// <summary>POST .../set-primary, .../archive and .../reactivate (FR-06, FR-07, BR-07).</summary>
public sealed class ChangeCustomerPropertyStateHandler(
    ICustomerDetailStore detailStore, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<CustomerResult<CustomerNoValue>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid customerId,
        Guid propertyId,
        CustomerPropertyAction action,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        var outcome = await detailStore.ChangePropertyStateAsync(
            organizationId,
            scope,
            customerId,
            propertyId,
            action,
            actorUserId,
            clientIp,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return outcome == CustomerPropertyOutcome.Changed
            ? CustomerResult<CustomerNoValue>.NoOp()
            : CustomerDetailChecks.Failure<CustomerNoValue>(outcome);
    }
}

/// <summary>GET /customers/{id}/recent-work (FR-10, BR-14).</summary>
public sealed class GetCustomerRecentWorkHandler(
    ICustomerStore customerStore, ICustomerDetailStore detailStore, IBranchScopeResolver scopeResolver)
{
    public async Task<CustomerRecentWork?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid customerId, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        var items = await detailStore.ListRecentWorkAsync(organizationId, scope, customerId, cancellationToken);

        if (items is null)
        {
            return null;
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);

        return new CustomerRecentWork(items, context.Currency, context.Timezone);
    }
}

/// <summary>GET /customers/{id}/upcoming-appointments (FR-11, BR-15).</summary>
public sealed class GetCustomerUpcomingAppointmentsHandler(
    ICustomerStore customerStore,
    ICustomerDetailStore detailStore,
    IBranchScopeResolver scopeResolver,
    TimeProvider timeProvider)
{
    public async Task<CustomerUpcomingAppointments?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid customerId, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        var items = await detailStore.ListUpcomingAppointmentsAsync(
            organizationId, scope, customerId, timeProvider.GetUtcNow(), cancellationToken);

        if (items is null)
        {
            return null;
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);

        return new CustomerUpcomingAppointments(items, context.Timezone);
    }
}

/// <summary>GET /customers/{id}/notes (FR-12, BR-16). The customer is resolved first, so a hidden id is 404 whatever the page is.</summary>
public sealed class ListCustomerNotesHandler(
    ICustomerStore customerStore, ICustomerDetailStore detailStore, IBranchScopeResolver scopeResolver)
{
    public async Task<CustomerResult<CustomerNotesPage>> HandleAsync(
        Guid organizationId, Guid membershipId, Guid customerId, string? pageText, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (!await detailStore.IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return CustomerResult<CustomerNotesPage>.NotFound();
        }

        if (CustomerPropertyRules.ParsePage(pageText) is not { } page)
        {
            return CustomerResult<CustomerNotesPage>.Invalid("page", CustomerMessages.PageInvalid);
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);
        var result = await detailStore.ListNotesAsync(organizationId, scope, customerId, page, context.Timezone, cancellationToken);

        return result is null ? CustomerResult<CustomerNotesPage>.NotFound() : CustomerResult<CustomerNotesPage>.Ok(result);
    }
}

/// <summary>POST /customers/{id}/notes (FR-12, FR-17, BR-16).</summary>
public sealed class AddCustomerNoteHandler(ICustomerDetailStore detailStore, IBranchScopeResolver scopeResolver)
{
    public async Task<CustomerResult<CustomerNoteView>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid customerId,
        Guid actorUserId,
        IPAddress? clientIp,
        string? note,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (!await detailStore.IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return CustomerResult<CustomerNoteView>.NotFound();
        }

        var text = CustomerPropertyRules.ValidateNote(note, out var error);

        if (text is null)
        {
            return CustomerResult<CustomerNoteView>.Invalid(CustomerPropertyRules.NoteKey, error!);
        }

        var created = await detailStore.AddNoteAsync(
            organizationId, scope, customerId, text, actorUserId, clientIp, cancellationToken);

        return created is null ? CustomerResult<CustomerNoteView>.NotFound() : CustomerResult<CustomerNoteView>.Ok(created);
    }
}

/// <summary>GET /customers/{id}/activity (FR-13, BR-17).</summary>
public sealed class ListCustomerActivityHandler(
    ICustomerStore customerStore, ICustomerDetailStore detailStore, IBranchScopeResolver scopeResolver)
{
    public async Task<CustomerResult<CustomerActivityPage>> HandleAsync(
        Guid organizationId, Guid membershipId, Guid customerId, string? pageText, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (!await detailStore.IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return CustomerResult<CustomerActivityPage>.NotFound();
        }

        if (CustomerPropertyRules.ParsePage(pageText) is not { } page)
        {
            return CustomerResult<CustomerActivityPage>.Invalid("page", CustomerMessages.PageInvalid);
        }

        var context = await CustomerDetailChecks.ContextAsync(customerStore, organizationId, cancellationToken);
        var result = await detailStore.ListActivityAsync(organizationId, scope, customerId, page, context.Timezone, cancellationToken);

        return result is null ? CustomerResult<CustomerActivityPage>.NotFound() : CustomerResult<CustomerActivityPage>.Ok(result);
    }
}
