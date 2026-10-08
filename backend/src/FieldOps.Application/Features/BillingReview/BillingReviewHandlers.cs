using System.Globalization;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.TechnicianVisits;

namespace FieldOps.Application.Features.BillingReview;

/// <summary>Parses the queue and export query (completed-jobs-review BR-04, BR-05); every invalid value is a field error.</summary>
public static class BillingQueryParser
{
    public static BillingOutcome<BillingQuery> Parse(BillingQueryText text, bool paged)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        Guid? branchId = null;

        if (!string.IsNullOrWhiteSpace(text.BranchId))
        {
            if (TryGuid(text.BranchId, out var parsed))
            {
                branchId = parsed;
            }
            else
            {
                errors["branchId"] = [BillingMessages.BranchInvalid];
            }
        }

        Guid? technicianId = null;

        if (!string.IsNullOrWhiteSpace(text.TechnicianId))
        {
            if (TryGuid(text.TechnicianId, out var parsed))
            {
                technicianId = parsed;
            }
            else
            {
                errors["technicianId"] = [BillingMessages.TechnicianInvalid];
            }
        }

        var completed = Choice(text.Completed, "30d", BillingCodes.CompletedRanges, "completed", BillingMessages.CompletedInvalid, errors);
        var variance = Choice(text.Variance, "all", BillingCodes.VarianceFilters, "variance", BillingMessages.VarianceInvalid, errors);
        var tab = Choice(text.Tab, "all", BillingCodes.Tabs, "tab", BillingMessages.TabInvalid, errors);
        var search = text.Search?.Trim();

        if (search is { Length: > BillingMessages.MaxSearchLength })
        {
            errors["search"] = [BillingMessages.SearchTooLong];
        }

        var page = 1;

        if (paged
            && !string.IsNullOrWhiteSpace(text.Page)
            && (!int.TryParse(text.Page.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors["page"] = [BillingMessages.PageInvalid];
        }

        return errors.Count > 0
            ? new BillingOutcome<BillingQuery>.Invalid(errors)
            : new BillingOutcome<BillingQuery>.Succeeded(new BillingQuery(
                branchId, completed, technicianId, variance, string.IsNullOrEmpty(search) ? null : search, tab, page));
    }

    private static bool TryGuid(string? text, out Guid id) => Guid.TryParse(text?.Trim(), out id) && id != Guid.Empty;

    private static string Choice(
        string? value, string fallback, string[] allowed, string key, string message, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim().ToLowerInvariant();

        if (allowed.Contains(normalized))
        {
            return normalized;
        }

        errors[key] = [message];

        return fallback;
    }
}

internal static class BillingHandlerSupport
{
    public static Task<BranchScope> ScopeAsync(IBranchScopeResolver scopes, MembershipCall call, CancellationToken cancellationToken) =>
        scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);

    public static BillingActor Actor(MembershipCall call, BranchScope scope) => new(call.OrganizationId, call.UserId, scope, call.IpAddress);
}

public sealed class GetBillingOptionsHandler(IBillingReviewStore store, IBranchScopeResolver scopes)
{
    public async Task<BillingOptionsView> HandleAsync(MembershipCall call, bool canAct, CancellationToken cancellationToken) =>
        await store.GetOptionsAsync(
            call.OrganizationId, await BillingHandlerSupport.ScopeAsync(scopes, call, cancellationToken), canAct, cancellationToken);
}

public sealed class GetBillingQueueHandler(IBillingReviewStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<BillingOutcome<BillingQueuePage>> HandleAsync(
        MembershipCall call, BillingQueryText text, CancellationToken cancellationToken)
    {
        var parsed = BillingQueryParser.Parse(text, paged: true);

        if (parsed is not BillingOutcome<BillingQuery>.Succeeded query)
        {
            return parsed switch
            {
                BillingOutcome<BillingQuery>.Invalid invalid => new BillingOutcome<BillingQueuePage>.Invalid(invalid.Errors),
                _ => new BillingOutcome<BillingQueuePage>.NotFound(),
            };
        }

        var scope = await BillingHandlerSupport.ScopeAsync(scopes, call, cancellationToken);

        return await store.GetQueueAsync(call.OrganizationId, scope, query.Value, timeProvider.GetUtcNow(), cancellationToken);
    }
}

/// <summary>Exports the filtered queue as CSV (BR-21): the filters and tab of the queue without paging.</summary>
public sealed class ExportBillingQueueHandler(IBillingReviewStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    private static readonly string[] Header =
    [
        "Work order", "Customer", "Job", "Branch", "Technician", "Completed at", "Approved total", "Currency",
        "Labor variance", "Material variance", "Ready", "Follow-up",
    ];

    public async Task<BillingOutcome<BillingCsvFile>> HandleAsync(
        MembershipCall call, BillingQueryText text, CancellationToken cancellationToken)
    {
        var parsed = BillingQueryParser.Parse(text, paged: false);

        if (parsed is not BillingOutcome<BillingQuery>.Succeeded query)
        {
            return parsed is BillingOutcome<BillingQuery>.Invalid invalid
                ? new BillingOutcome<BillingCsvFile>.Invalid(invalid.Errors)
                : new BillingOutcome<BillingCsvFile>.NotFound();
        }

        var scope = await BillingHandlerSupport.ScopeAsync(scopes, call, cancellationToken);

        switch (await store.ExportAsync(call.OrganizationId, scope, query.Value, timeProvider.GetUtcNow(), cancellationToken))
        {
            case BillingOutcome<BillingExport>.Succeeded export:
                return new BillingOutcome<BillingCsvFile>.Succeeded(Compose(export.Value));
            case BillingOutcome<BillingExport>.Invalid invalid:
                return new BillingOutcome<BillingCsvFile>.Invalid(invalid.Errors);
            case BillingOutcome<BillingExport>.Conflict conflict:
                return new BillingOutcome<BillingCsvFile>.Conflict(conflict.Code, conflict.Title);
            default:
                return new BillingOutcome<BillingCsvFile>.NotFound();
        }
    }

    private static BillingCsvFile Compose(BillingExport export)
    {
        var zone = OrganizationTime.FindZone(export.Timezone);
        var rows = new List<string> { CsvCell.Row(Header) };

        foreach (var row in export.Rows)
        {
            rows.Add(CsvCell.Row(
            [
                row.Number,
                row.Customer,
                row.Job,
                row.Branch,
                row.Technician,
                OrganizationTime.ToZone(row.CompletedAt, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                row.ApprovedTotal.ToString("0.00", CultureInfo.InvariantCulture),
                row.Currency,
                row.LaborVariance switch
                {
                    BillingCodes.LaborOver => "Over",
                    BillingCodes.LaborUnder => "Under",
                    _ => "None",
                },
                YesNo(row.MaterialVariance),
                YesNo(row.Ready),
                YesNo(row.FollowUp),
            ]));
        }

        return new BillingCsvFile(
            $"completed-jobs-review-{export.OrganizationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv",
            CsvCell.Document(rows));
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";
}

public sealed class GetBillingDetailHandler(IBillingReviewStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<BillingReviewDetail?> HandleAsync(
        MembershipCall call, Guid workOrderId, bool canAct, CancellationToken cancellationToken) =>
        await store.GetDetailAsync(
            call.OrganizationId,
            await BillingHandlerSupport.ScopeAsync(scopes, call, cancellationToken),
            workOrderId,
            canAct,
            timeProvider.GetUtcNow(),
            cancellationToken);
}

public sealed class GetBillingEvidenceHandler(IBillingReviewStore store, IBranchScopeResolver scopes)
{
    public async Task<VisitEvidenceImage?> HandleAsync(
        MembershipCall call, Guid workOrderId, Guid evidenceId, CancellationToken cancellationToken) =>
        await store.GetEvidenceAsync(
            call.OrganizationId,
            await BillingHandlerSupport.ScopeAsync(scopes, call, cancellationToken),
            workOrderId,
            evidenceId,
            cancellationToken);
}

/// <summary>PATCH review (BR-15): 404 for anything outside the queue, 400 for the shape, then the locked transaction.</summary>
public sealed class UpdateBillingReviewHandler(IBillingReviewStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<BillingOutcome<ReviewResult>> HandleAsync(
        MembershipCall call, Guid workOrderId, ReviewBodyText body, CancellationToken cancellationToken)
    {
        var scope = await BillingHandlerSupport.ScopeAsync(scopes, call, cancellationToken);

        if (await store.GetAccessAsync(call.OrganizationId, scope, workOrderId, cancellationToken) != BillingAccess.QueueMember)
        {
            return new BillingOutcome<ReviewResult>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var note = NormalizeNote(body.Note, errors);
        var reason = string.IsNullOrWhiteSpace(body.Reason) ? BillingCodes.ReasonManual : body.Reason.Trim();

        if (reason is not (BillingCodes.ReasonManual or BillingCodes.ReasonReturnedToQueue))
        {
            errors["reason"] = [BillingMessages.ReasonInvalid];
        }

        if (!body.NoteSent && body.FollowUp is null)
        {
            errors["body"] = [BillingMessages.ReviewEmpty];
        }

        if (errors.Count > 0)
        {
            return new BillingOutcome<ReviewResult>.Invalid(errors);
        }

        return await store.UpdateReviewAsync(
            BillingHandlerSupport.Actor(call, scope),
            workOrderId,
            new ReviewInput(body.NoteSent, note, body.FollowUp, reason),
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    internal static string? NormalizeNote(string? note, Dictionary<string, string[]> errors)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (trimmed is { Length: > BillingMessages.MaxNoteLength })
        {
            errors["note"] = [BillingMessages.NoteTooLong];
        }

        return trimmed;
    }
}

/// <summary>POST invoice (BR-17 to BR-20): 404, then the BR-17 body, then the locked, idempotent transaction in the store.</summary>
public sealed class GenerateBillingInvoiceHandler(IBillingReviewStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<BillingOutcome<GenerateResult>> HandleAsync(
        MembershipCall call, Guid workOrderId, GenerateBodyText body, CancellationToken cancellationToken)
    {
        var scope = await BillingHandlerSupport.ScopeAsync(scopes, call, cancellationToken);

        if (await store.GetAccessAsync(call.OrganizationId, scope, workOrderId, cancellationToken) == BillingAccess.NotVisible)
        {
            return new BillingOutcome<GenerateResult>.NotFound();
        }

        var now = timeProvider.GetUtcNow();
        var organization = await store.GetOrganizationAsync(call.OrganizationId, cancellationToken);
        var today = OrganizationTime.LocalDate(now, OrganizationTime.FindZone(organization.Timezone));
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!DateOnly.TryParseExact(body.IssueDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var issueDate)
            || issueDate > today
            || issueDate < today.AddDays(-BillingMessages.IssueDateWindowDays))
        {
            errors["issueDate"] = [BillingMessages.IssueDateInvalid];
        }

        var terms = body.PaymentTerms?.Trim();

        if (!BillingTerms.IsValid(terms))
        {
            errors["paymentTerms"] = [BillingMessages.PaymentTermsInvalid];
        }

        var note = UpdateBillingReviewHandler.NormalizeNote(body.Note, errors);

        if (body.AcknowledgeVariances is null)
        {
            errors["acknowledgeVariances"] = [BillingMessages.AcknowledgeRequired];
        }

        if (errors.Count > 0)
        {
            return new BillingOutcome<GenerateResult>.Invalid(errors);
        }

        return await store.GenerateInvoiceAsync(
            BillingHandlerSupport.Actor(call, scope),
            workOrderId,
            new GenerateInput(issueDate, terms!, note, body.AcknowledgeVariances!.Value),
            now,
            cancellationToken);
    }
}
