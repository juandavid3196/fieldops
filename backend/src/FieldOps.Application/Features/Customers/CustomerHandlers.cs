using System.Net;
using FieldOps.Application.Features.Access;
using FieldOps.Domain.Customers;

namespace FieldOps.Application.Features.Customers;

/// <summary>Month bounds in the organization time zone, as UTC instants (BR-05).</summary>
public static class CustomerMonth
{
    public static (DateTimeOffset Start, DateTimeOffset NextStart) Bounds(string? timezone, DateTimeOffset now)
    {
        var zone = FindZone(timezone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var startLocal = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);

        return (ToUtc(startLocal, zone), ToUtc(startLocal.AddMonths(1), zone));
    }

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone)
    {
        // Midnight can fall in a DST gap: use the offset in force an hour later.
        var offset = zone.IsInvalidTime(local) ? zone.GetUtcOffset(local.AddHours(1)) : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static TimeZoneInfo FindZone(string? timezone)
    {
        try
        {
            return timezone is null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

internal static class CustomerChecks
{
    public static Dictionary<string, string[]> ToErrors(IReadOnlyDictionary<string, string> errors) =>
        errors.ToDictionary(pair => pair.Key, pair => new[] { pair.Value }, StringComparer.Ordinal);

    /// <summary>
    /// Verifies the raw branch and tag ids against the organization and the caller's scope. Unknown,
    /// foreign and out-of-scope branches share one message so nothing leaks (BR-02, BR-12).
    /// </summary>
    public static async Task<(Guid? BranchId, IReadOnlyList<Guid> TagIds)> ResolveBranchAndTagsAsync(
        ICustomerStore store,
        Guid organizationId,
        BranchScope scope,
        string? branchText,
        IReadOnlyList<string>? tagTexts,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        Guid? branchId = null;

        if (string.IsNullOrWhiteSpace(branchText))
        {
            errors[CustomerRules.BranchIdKey] = [CustomerMessages.BranchRequired];
        }
        else if (!CustomerQueryParser.TryParseGuid(branchText, out var parsedBranch)
            || !await store.IsBranchAllowedAsync(organizationId, scope, parsedBranch, requireActive: true, cancellationToken))
        {
            errors[CustomerRules.BranchIdKey] = [CustomerMessages.BranchNotAllowed];
        }
        else
        {
            branchId = parsedBranch;
        }

        var tagIds = new List<Guid>();
        var tagsValid = true;

        foreach (var text in tagTexts ?? [])
        {
            if (CustomerQueryParser.TryParseGuid(text, out var tagId))
            {
                if (!tagIds.Contains(tagId))
                {
                    tagIds.Add(tagId);
                }
            }
            else
            {
                tagsValid = false;
            }
        }

        if (!tagsValid)
        {
            errors[CustomerRules.TagIdsKey] = [CustomerMessages.TagInvalid];
        }
        else if (tagIds.Count > CustomerRules.MaxTags)
        {
            errors[CustomerRules.TagIdsKey] = [CustomerMessages.TagsTooMany];
        }
        else if (tagIds.Count > 0 && await store.CountTagsAsync(organizationId, tagIds, cancellationToken) != tagIds.Count)
        {
            errors[CustomerRules.TagIdsKey] = [CustomerMessages.TagInvalid];
        }

        return (branchId, tagIds);
    }
}

/// <summary>GET /customers (FR-03, BR-06 to BR-08).</summary>
public sealed class ListCustomersHandler(ICustomerStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<CustomerResult<CustomerListPage>> HandleAsync(
        Guid organizationId, Guid membershipId, CustomerListQuery query, CancellationToken cancellationToken)
    {
        var filter = CustomerQueryParser.Parse(query, out var errors);
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (filter?.BranchId is { } branchId
            && !await store.IsBranchAllowedAsync(organizationId, scope, branchId, requireActive: false, cancellationToken))
        {
            errors[CustomerRules.BranchIdKey] = [CustomerMessages.BranchNotAllowed];
            filter = null;
        }

        if (filter is null || errors.Count > 0)
        {
            return CustomerResult<CustomerListPage>.Invalid(errors);
        }

        var context = await store.GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The session organization does not exist.");

        var data = await store.ListAsync(organizationId, scope, filter, timeProvider.GetUtcNow(), cancellationToken);

        return CustomerResult<CustomerListPage>.Ok(new CustomerListPage(
            data.Items,
            data.TotalCount,
            filter.Page,
            CustomerQueryParser.PageSize,
            data.TabCounts,
            context.Currency,
            context.Timezone));
    }
}

/// <summary>GET /customers/metrics (FR-02, BR-05).</summary>
public sealed class GetCustomerMetricsHandler(ICustomerStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<CustomerResult<CustomerMetrics>> HandleAsync(
        Guid organizationId, Guid membershipId, string? branchIdText, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        Guid? branchId = null;

        if (!string.IsNullOrEmpty(branchIdText))
        {
            if (!CustomerQueryParser.TryParseGuid(branchIdText, out var parsed)
                || !await store.IsBranchAllowedAsync(organizationId, scope, parsed, requireActive: false, cancellationToken))
            {
                return CustomerResult<CustomerMetrics>.Invalid(CustomerRules.BranchIdKey, CustomerMessages.BranchNotAllowed);
            }

            branchId = parsed;
        }

        var context = await store.GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The session organization does not exist.");

        var (start, nextStart) = CustomerMonth.Bounds(context.Timezone, timeProvider.GetUtcNow());
        var data = await store.GetMetricsAsync(organizationId, scope, branchId, start, nextStart, cancellationToken);

        return CustomerResult<CustomerMetrics>.Ok(new CustomerMetrics(
            data.Total, data.Active, data.NewThisMonth, data.Outstanding, context.Currency));
    }
}

/// <summary>GET /customers/{id} (FR-07). Another organization's or an out-of-scope customer is null.</summary>
public sealed class GetCustomerHandler(ICustomerStore store, IBranchScopeResolver scopeResolver)
{
    public async Task<CustomerDetail?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid customerId, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        return await store.GetDetailAsync(organizationId, scope, customerId, cancellationToken);
    }
}

/// <summary>POST /customers (FR-05, BR-10 to BR-13).</summary>
public sealed class CreateCustomerHandler(ICustomerStore store, IBranchScopeResolver scopeResolver)
{
    public async Task<CustomerResult<Guid>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid actorUserId,
        IPAddress? clientIp,
        CustomerWriteInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        var context = await store.GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The session organization does not exist.");

        var values = CustomerRules.Validate(
            input.Fields, null, CustomerRules.IsUnitedStates(context.CountryCode), out var fieldErrors);
        var errors = CustomerChecks.ToErrors(fieldErrors);

        var (branchId, tagIds) = await CustomerChecks.ResolveBranchAndTagsAsync(
            store, organizationId, scope, input.BranchId, input.TagIds, errors, cancellationToken);

        if (values is null || branchId is null || errors.Count > 0)
        {
            return CustomerResult<Guid>.Invalid(errors);
        }

        var countryCode = string.IsNullOrWhiteSpace(context.CountryCode) ? "US" : context.CountryCode.Trim().ToUpperInvariant();

        return CustomerResult<Guid>.Ok(await store.CreateAsync(
            organizationId,
            new CustomerWrite(values, branchId.Value, tagIds, countryCode),
            actorUserId,
            clientIp,
            cancellationToken));
    }
}

/// <summary>PUT /customers/{id} (FR-07, BR-10, BR-11). The customer is resolved first, so a hidden id is 404 whatever the body is.</summary>
public sealed class UpdateCustomerHandler(ICustomerStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<CustomerResult<CustomerDetail>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid customerId,
        Guid actorUserId,
        IPAddress? clientIp,
        CustomerWriteInput input,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (await store.GetTypeAsync(organizationId, scope, customerId, cancellationToken) is null)
        {
            return CustomerResult<CustomerDetail>.NotFound();
        }

        var context = await store.GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The session organization does not exist.");

        var values = CustomerRules.Validate(
            input.Fields, null, CustomerRules.IsUnitedStates(context.CountryCode), out var fieldErrors);
        var errors = CustomerChecks.ToErrors(fieldErrors);

        var (branchId, tagIds) = await CustomerChecks.ResolveBranchAndTagsAsync(
            store, organizationId, scope, input.BranchId, input.TagIds, errors, cancellationToken);

        if (values is null || branchId is null || errors.Count > 0)
        {
            return CustomerResult<CustomerDetail>.Invalid(errors);
        }

        var detail = await store.UpdateAsync(
            organizationId,
            scope,
            customerId,
            values,
            branchId.Value,
            tagIds,
            actorUserId,
            clientIp,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return detail is null ? CustomerResult<CustomerDetail>.NotFound() : CustomerResult<CustomerDetail>.Ok(detail);
    }
}

/// <summary>POST /customers/{id}/archive and /reactivate (FR-08, BR-16).</summary>
public sealed class SetCustomerActiveHandler(ICustomerStore store, IBranchScopeResolver scopeResolver, TimeProvider timeProvider)
{
    public async Task<CustomerResult<CustomerNoValue>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid customerId,
        bool isActive,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);

        var outcome = await store.SetActiveAsync(
            organizationId, scope, customerId, isActive, actorUserId, clientIp, timeProvider.GetUtcNow(), cancellationToken);

        return outcome switch
        {
            CustomerStateOutcome.NotFound => CustomerResult<CustomerNoValue>.NotFound(),
            CustomerStateOutcome.NoChange => CustomerResult<CustomerNoValue>.Conflict(
                isActive ? CustomerMessages.AlreadyActive : CustomerMessages.AlreadyArchived),
            _ => CustomerResult<CustomerNoValue>.NoOp(),
        };
    }
}

/// <summary>POST /customers/duplicate-check (FR-06, BR-14). Never rejects; out-of-scope matches are redacted.</summary>
public sealed class CheckCustomerDuplicatesHandler(ICustomerStore store, IBranchScopeResolver scopeResolver)
{
    public async Task<CustomerDuplicateCheck> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        string? email,
        string? phone,
        string? excludeCustomerId,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = CustomerNormalizer.NormalizeEmail(email);

        if (!CustomerNormalizer.IsValidEmail(normalizedEmail))
        {
            normalizedEmail = null;
        }

        if (!CustomerNormalizer.TryNormalizePhone(phone, out var normalizedPhone))
        {
            normalizedPhone = null;
        }

        if (normalizedEmail is null && normalizedPhone is null)
        {
            return new CustomerDuplicateCheck([]);
        }

        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        Guid? exclude = null;

        // Ignored without error unless it is a customer the caller can see.
        if (CustomerQueryParser.TryParseGuid(excludeCustomerId, out var excludeId)
            && await store.GetTypeAsync(organizationId, scope, excludeId, cancellationToken) is not null)
        {
            exclude = excludeId;
        }

        var rows = await store.FindDuplicatesAsync(organizationId, normalizedEmail, normalizedPhone, exclude, cancellationToken);

        return new CustomerDuplicateCheck([.. rows.Select(row =>
        {
            var field = row.EmailMatched ? "email" : "phone";

            return scope.Contains(row.BranchId)
                ? new CustomerDuplicateMatch(
                    row.CustomerId,
                    row.DisplayName,
                    row.PrimaryEmail,
                    CustomerNormalizer.FormatPhone(row.PrimaryPhone),
                    row.PropertyCount,
                    row.DisplayStatus,
                    field,
                    true)
                : new CustomerDuplicateMatch(null, row.DisplayName, null, null, null, row.DisplayStatus, field, false);
        })]);
    }
}

/// <summary>GET /customers/branch-options (FR-14, BR-02).</summary>
public sealed class GetCustomerBranchOptionsHandler(ICustomerStore store, IBranchScopeResolver scopeResolver)
{
    public async Task<CustomerBranchOptions> HandleAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        var context = await store.GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The session organization does not exist.");
        var branches = await store.ListBranchOptionsAsync(organizationId, scope, cancellationToken);

        return new CustomerBranchOptions(
            string.IsNullOrWhiteSpace(context.CountryCode) ? "US" : context.CountryCode, branches);
    }
}

/// <summary>GET /customer-tags (FR-09).</summary>
public sealed class ListCustomerTagsHandler(ICustomerStore store)
{
    public Task<IReadOnlyList<CustomerTagView>> HandleAsync(Guid organizationId, CancellationToken cancellationToken) =>
        store.ListTagsAsync(organizationId, cancellationToken);
}

/// <summary>POST /customer-tags (FR-09, BR-12): 200 for a case-insensitive match, 201 for a new tag.</summary>
public sealed class CreateCustomerTagHandler(ICustomerStore store)
{
    public async Task<CustomerResult<(CustomerTagView Tag, bool Created)>> HandleAsync(
        Guid organizationId, Guid actorUserId, IPAddress? clientIp, string? name, CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return CustomerResult<(CustomerTagView, bool)>.Invalid("name", CustomerMessages.TagNameRequired);
        }

        if (trimmed.Length > CustomerTag.NameMaxLength)
        {
            return CustomerResult<(CustomerTagView, bool)>.Invalid("name", CustomerMessages.TagNameTooLong);
        }

        return CustomerResult<(CustomerTagView, bool)>.Ok(
            await store.GetOrCreateTagAsync(organizationId, trimmed, actorUserId, clientIp, cancellationToken));
    }
}
