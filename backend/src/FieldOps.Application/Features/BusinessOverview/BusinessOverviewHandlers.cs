using FieldOps.Application.Features.AuditTrail;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.BusinessOverview;

internal sealed record BusinessPrepared(BusinessOverviewQuery Query, BusinessRequest Request, BusinessOrganization Organization);

internal static class BusinessOverviewSupport
{
    /// <summary>
    /// Validates the query text and the range against the organization-local today, then authorizes the branch. A 400 comes
    /// first; an unknown, inactive or foreign branch is one identical not found (BR-02, BR-03). The organization is the session's.
    /// </summary>
    public static async Task<BillingOutcome<BusinessPrepared>>
        PrepareAsync(
            IBusinessOverviewStore store,
            MembershipCall call,
            BusinessOverviewQueryText text,
            bool withCompare,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        var parsed = BusinessOverviewRules.Parse(text, withCompare);
        var organization = await store.GetOrganizationAsync(call.OrganizationId, cancellationToken);
        var today = OrganizationTime.LocalDate(now, OrganizationTime.FindZone(organization.Timezone));
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (parsed is BillingOutcome<BusinessRequest>.Invalid invalid)
        {
            foreach (var (key, value) in invalid.Errors)
            {
                errors[key] = value;
            }
        }

        if (parsed is BillingOutcome<BusinessRequest>.Succeeded succeeded)
        {
            foreach (var (key, value) in BusinessOverviewRules.ValidateRange(succeeded.Value.From, succeeded.Value.To, today))
            {
                errors[key] = value;
            }
        }

        if (errors.Count > 0 || parsed is not BillingOutcome<BusinessRequest>.Succeeded ok)
        {
            return new BillingOutcome<BusinessPrepared>.Invalid(errors);
        }

        var request = ok.Value;

        if (request.BranchId is { } branchId && !await store.BranchIsActiveAsync(call.OrganizationId, branchId, cancellationToken))
        {
            return new BillingOutcome<BusinessPrepared>.NotFound();
        }

        var query = new BusinessOverviewQuery(
            organization.Timezone, BusinessOverviewRules.ResolvePeriod(request.From, request.To), request.BranchId, today);

        return new BillingOutcome<BusinessPrepared>.Succeeded(
            new BusinessPrepared(query, request, organization));
    }

    public static async Task<BusinessOverviewView> BuildAsync(
        IBusinessOverviewStore store,
        MembershipCall call,
        BusinessOverviewQuery query,
        BusinessOrganization organization,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var source = await store.LoadAsync(call.OrganizationId, query, cancellationToken);

        return BusinessOverviewBuilder.Build(source, query, organization.Currency, now);
    }
}

public sealed class GetBusinessOptionsHandler(IBusinessOverviewStore store, TimeProvider timeProvider)
{
    public async Task<BusinessOptionsView> HandleAsync(MembershipCall call, CancellationToken cancellationToken)
    {
        var organization = await store.GetOrganizationAsync(call.OrganizationId, cancellationToken);
        var branches = await store.GetActiveBranchesAsync(call.OrganizationId, cancellationToken);
        var today = OrganizationTime.LocalDate(timeProvider.GetUtcNow(), OrganizationTime.FindZone(organization.Timezone));

        return new BusinessOptionsView(
            organization.Timezone, organization.Currency, today, BusinessOverviewMessages.MarginTarget, branches);
    }
}

public sealed class GetBusinessOverviewHandler(IBusinessOverviewStore store, TimeProvider timeProvider)
{
    public async Task<BillingOutcome<BusinessOverviewView>> HandleAsync(
        MembershipCall call, BusinessOverviewQueryText text, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var prepared = await BusinessOverviewSupport.PrepareAsync(store, call, text, false, now, cancellationToken);

        return prepared switch
        {
            BillingOutcome<BusinessPrepared>.Succeeded ok =>
                new BillingOutcome<BusinessOverviewView>.Succeeded(
                    await BusinessOverviewSupport.BuildAsync(store, call, ok.Value.Query, ok.Value.Organization, now, cancellationToken)),
            BillingOutcome<BusinessPrepared>.Invalid invalid =>
                new BillingOutcome<BusinessOverviewView>.Invalid(invalid.Errors),
            _ => new BillingOutcome<BusinessOverviewView>.NotFound(),
        };
    }
}

public sealed class ExportBusinessOverviewHandler(IBusinessOverviewStore store, IExportAuditWriter audit, TimeProvider timeProvider)
{
    public async Task<BillingOutcome<BusinessExportFile>> HandleAsync(
        MembershipCall call, BusinessOverviewQueryText text, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var prepared = await BusinessOverviewSupport.PrepareAsync(store, call, text, true, now, cancellationToken);

        if (prepared is BillingOutcome<BusinessPrepared>.Succeeded ok)
        {
            var view = await BusinessOverviewSupport.BuildAsync(store, call, ok.Value.Query, ok.Value.Organization, now, cancellationToken);

            var file = BusinessOverviewCsv.Build(view, ok.Value.Request.Compare);
            var request = ok.Value.Request;

            // audit-trail BR-16: after the CSV is built, before it is returned; a failed insert is a 500 with no file.
            await audit.RecordAsync(
                call,
                ExportAudit.Entry(
                    ExportReportTypes.BusinessOverview,
                    request.BranchId,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["from"] = request.From,
                        ["to"] = request.To,
                        ["branchId"] = request.BranchId,
                        ["compare"] = request.Compare,
                        ["hasSearch"] = false,
                    },
                    file.RowCount),
                cancellationToken);

            return new BillingOutcome<BusinessExportFile>.Succeeded(file);
        }

        return prepared is BillingOutcome<BusinessPrepared>.Invalid invalid
            ? new BillingOutcome<BusinessExportFile>.Invalid(invalid.Errors)
            : new BillingOutcome<BusinessExportFile>.NotFound();
    }
}
