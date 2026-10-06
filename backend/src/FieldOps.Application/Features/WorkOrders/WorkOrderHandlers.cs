using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Validation;

namespace FieldOps.Application.Features.WorkOrders;

/// <summary>Shared steps of the work order handlers: scope, visibility (404 first), shape validation and outcome mapping.</summary>
internal static class WorkOrderHandlerSupport
{
    public static async Task<QuoteActor> ActorAsync(
        IBranchScopeResolver scopes, MembershipCall call, CancellationToken cancellationToken) =>
        new(
            call.OrganizationId,
            call.UserId,
            await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken),
            call.IpAddress);

    public static ServiceRequestResult<T> Map<T>(WorkOrderOutcome<T> outcome) =>
        outcome switch
        {
            WorkOrderOutcome<T>.Succeeded succeeded => ServiceRequestResult<T>.Ok(succeeded.Value),
            WorkOrderOutcome<T>.Conflict conflict => ServiceRequestResult<T>.Conflict(
                WorkOrderMessages.ConflictTitle(conflict.Code), conflict.Code),
            WorkOrderOutcome<T>.Invalid invalid => ServiceRequestResult<T>.Invalid(invalid.Errors),
            _ => ServiceRequestResult<T>.NotFound(),
        };

    /// <summary>A null token is valid (no saved order yet); a present one must parse.</summary>
    public static bool TryParseUpdatedAt(string? value, Dictionary<string, string[]> errors, out DateTimeOffset? token)
    {
        token = null;

        if (value is null)
        {
            return true;
        }

        if (UpdatedAtValidation.TryParse(value, out var parsed))
        {
            token = parsed;

            return true;
        }

        errors["updatedAt"] = [WorkOrderMessages.UpdatedAtMessage];

        return false;
    }
}

public sealed class GetWorkOrderEditorHandler(IWorkOrderStore store, IBranchScopeResolver scopes)
{
    public async Task<ServiceRequestResult<WorkOrderEditor>> HandleAsync(
        MembershipCall call, Guid quoteId, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);

        return WorkOrderHandlerSupport.Map(await store.GetEditorAsync(call.OrganizationId, scope, quoteId, cancellationToken));
    }
}

public sealed class SaveWorkOrderDraftHandler(IWorkOrderStore store, IBranchScopeResolver scopes)
{
    public async Task<ServiceRequestResult<WorkOrderDraftSaved>> HandleAsync(
        MembershipCall call, Guid quoteId, string? updatedAt, WorkOrderText body, CancellationToken cancellationToken)
    {
        var actor = await WorkOrderHandlerSupport.ActorAsync(scopes, call, cancellationToken);

        if (!(await store.LookupAsync(call.OrganizationId, actor.Scope, quoteId, cancellationToken)).Visible)
        {
            return ServiceRequestResult<WorkOrderDraftSaved>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var tokenValid = WorkOrderHandlerSupport.TryParseUpdatedAt(updatedAt, errors, out var token);
        var input = WorkOrderValidator.Validate(body, errors);

        return input is null || !tokenValid
            ? ServiceRequestResult<WorkOrderDraftSaved>.Invalid(errors)
            : WorkOrderHandlerSupport.Map(await store.SaveDraftAsync(actor, quoteId, token, input, cancellationToken));
    }
}

/// <summary>Create (BR-18, BR-20): an already created order answers first and ignores the body; otherwise validation, then one transaction.</summary>
public sealed class CreateWorkOrderHandler(IWorkOrderStore store, IBranchScopeResolver scopes)
{
    public async Task<ServiceRequestResult<WorkOrderCreated>> HandleAsync(
        MembershipCall call, Guid quoteId, string? updatedAt, WorkOrderText body, CancellationToken cancellationToken)
    {
        var actor = await WorkOrderHandlerSupport.ActorAsync(scopes, call, cancellationToken);
        var lookup = await store.LookupAsync(call.OrganizationId, actor.Scope, quoteId, cancellationToken);

        if (!lookup.Visible)
        {
            return ServiceRequestResult<WorkOrderCreated>.NotFound();
        }

        if (lookup.Created is { } existing)
        {
            return ServiceRequestResult<WorkOrderCreated>.Ok(new WorkOrderCreated(existing, false));
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var tokenValid = WorkOrderHandlerSupport.TryParseUpdatedAt(updatedAt, errors, out var token);
        var input = WorkOrderValidator.Validate(body, errors);

        return input is null || !tokenValid
            ? ServiceRequestResult<WorkOrderCreated>.Invalid(errors)
            : WorkOrderHandlerSupport.Map(await store.CreateAsync(actor, quoteId, token, input, cancellationToken));
    }
}

public sealed class ListWorkOrdersHandler(IWorkOrderStore store, IBranchScopeResolver scopes)
{
    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;

    public async Task<ServiceRequestResult<WorkOrderPage>> HandleAsync(
        MembershipCall call, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var pageNumber = page ?? 1;
        var size = pageSize ?? DefaultPageSize;

        if (pageNumber < 1)
        {
            errors["page"] = ["Page must be 1 or greater."];
        }

        if (size is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["Page size must be between 1 and 100."];
        }

        if (errors.Count > 0)
        {
            return ServiceRequestResult<WorkOrderPage>.Invalid(errors);
        }

        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);

        return ServiceRequestResult<WorkOrderPage>.Ok(
            await store.ListAsync(call.OrganizationId, scope, pageNumber, size, cancellationToken));
    }
}

public sealed class GetWorkOrderHandler(IWorkOrderStore store, IBranchScopeResolver scopes)
{
    public async Task<WorkOrderDetail?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid workOrderId, bool canManage, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(organizationId, membershipId, cancellationToken);

        return await store.GetAsync(organizationId, scope, workOrderId, canManage, cancellationToken);
    }
}
