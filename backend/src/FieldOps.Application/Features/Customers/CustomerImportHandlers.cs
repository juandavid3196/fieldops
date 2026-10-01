using System.Net;
using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.Customers;

/// <summary>The outcome of parsing and resolving an import file; shared by preview and confirm.</summary>
internal sealed record CustomerImportPlan(
    string? FileError,
    IReadOnlyList<CustomerRowError> RowErrors,
    IReadOnlyList<CustomerCsvRow> ValidRows,
    IReadOnlyDictionary<int, Guid> BranchByLine,
    string CountryCode);

internal static class CustomerImportPlanner
{
    public static async Task<CustomerImportPlan> PlanAsync(
        ICustomerStore store,
        Guid organizationId,
        BranchScope scope,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var context = await store.GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The session organization does not exist.");

        var parse = CustomerCsv.Parse(content, CustomerRules.IsUnitedStates(context.CountryCode));
        var countryCode = string.IsNullOrWhiteSpace(context.CountryCode) ? "US" : context.CountryCode.Trim().ToUpperInvariant();

        if (parse.FileError is not null)
        {
            return new CustomerImportPlan(parse.FileError, [], [], new Dictionary<int, Guid>(), countryCode);
        }

        var errors = new List<CustomerRowError>(parse.RowErrors);
        var codes = parse.Rows
            .Select(row => row.BranchCode.ToLowerInvariant())
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var branches = codes.Length == 0
            ? new Dictionary<string, Guid>(StringComparer.Ordinal)
            : await store.FindActiveBranchesByCodeAsync(organizationId, scope, codes, cancellationToken);

        var valid = new List<CustomerCsvRow>();
        var branchByLine = new Dictionary<int, Guid>();

        foreach (var row in parse.Rows)
        {
            if (branches.TryGetValue(row.BranchCode.ToLowerInvariant(), out var branchId))
            {
                valid.Add(row);
                branchByLine[row.Line] = branchId;
            }
            else
            {
                errors.Add(new CustomerRowError(row.Line, "branch_code", CustomerCsv.BranchCodeMessage));
            }
        }

        // A row that fails any rule is not valid, whichever rule it failed.
        var failedLines = errors.Select(error => error.Row).ToHashSet();

        return new CustomerImportPlan(
            null,
            CustomerCsv.Finalize(errors),
            [.. valid.Where(row => !failedLines.Contains(row.Line))],
            branchByLine,
            countryCode);
    }
}

/// <summary>POST /customers/import/preview (FR-10, BR-17): validates and reports duplicates; writes nothing.</summary>
public sealed class PreviewCustomerImportHandler(ICustomerStore store, IBranchScopeResolver scopeResolver)
{
    public async Task<CustomerResult<CustomerImportPreview>> HandleAsync(
        Guid organizationId, Guid membershipId, byte[] content, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        var plan = await CustomerImportPlanner.PlanAsync(store, organizationId, scope, content, cancellationToken);

        if (plan.FileError is not null)
        {
            return CustomerResult<CustomerImportPreview>.Invalid("file", plan.FileError);
        }

        var existing = await store.FindContactMatchesAsync(
            organizationId,
            [.. plan.ValidRows.Select(row => row.Values.Email).Distinct(StringComparer.Ordinal)],
            [.. plan.ValidRows.Select(row => row.Values.Phone).OfType<string>().Distinct(StringComparer.Ordinal)],
            cancellationToken);

        var existingByEmail = new Dictionary<string, string>(StringComparer.Ordinal);
        var existingByPhone = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var match in existing)
        {
            if (match.Email is not null)
            {
                existingByEmail.TryAdd(match.Email, match.DisplayName);
            }

            if (match.Phone is not null)
            {
                existingByPhone.TryAdd(match.Phone, match.DisplayName);
            }
        }

        var earlierByEmail = new Dictionary<string, string>(StringComparer.Ordinal);
        var earlierByPhone = new Dictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<CustomerImportDuplicateWarning>();

        foreach (var row in plan.ValidRows.OrderBy(row => row.Line))
        {
            var email = row.Values.Email;
            var phone = row.Values.Phone;

            if (existingByEmail.TryGetValue(email, out var name) || earlierByEmail.TryGetValue(email, out name))
            {
                warnings.Add(new CustomerImportDuplicateWarning(row.Line, "email", name));
            }
            else if (phone is not null
                && (existingByPhone.TryGetValue(phone, out name) || earlierByPhone.TryGetValue(phone, out name)))
            {
                warnings.Add(new CustomerImportDuplicateWarning(row.Line, "phone", name));
            }

            earlierByEmail.TryAdd(email, row.Values.DisplayName);

            if (phone is not null)
            {
                earlierByPhone.TryAdd(phone, row.Values.DisplayName);
            }
        }

        return CustomerResult<CustomerImportPreview>.Ok(
            new CustomerImportPreview(plan.ValidRows.Count, plan.RowErrors, warnings));
    }
}

/// <summary>POST /customers/import (FR-10, BR-17): all rows are created or none.</summary>
public sealed class ImportCustomersHandler(ICustomerStore store, IBranchScopeResolver scopeResolver)
{
    public async Task<CustomerResult<int>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid actorUserId,
        IPAddress? clientIp,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ResolveAsync(organizationId, membershipId, cancellationToken);
        var plan = await CustomerImportPlanner.PlanAsync(store, organizationId, scope, content, cancellationToken);

        if (plan.FileError is not null)
        {
            return CustomerResult<int>.Invalid("file", plan.FileError);
        }

        if (plan.RowErrors.Count > 0)
        {
            return CustomerResult<int>.InvalidRows(plan.RowErrors);
        }

        var rows = plan.ValidRows
            .Select(row => new CustomerImportRow(row.Values, plan.BranchByLine[row.Line], row.TagNames))
            .ToList();

        return CustomerResult<int>.Ok(await store.ImportAsync(
            organizationId, rows, plan.CountryCode, actorUserId, clientIp, cancellationToken));
    }
}
