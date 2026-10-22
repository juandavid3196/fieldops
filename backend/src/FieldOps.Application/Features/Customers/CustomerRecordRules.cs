using System.Globalization;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.WorkOrders;

namespace FieldOps.Application.Features.Customers;

public enum CustomerRecordKind
{
    Requests,
    Quotes,
    Jobs,
    Invoices,
}

/// <summary>Common list rules of the four related tabs (BR-04): one parser for search, status, page and page size.</summary>
public static class CustomerRecordRules
{
    public const int SearchMaxLength = 100;

    public const int DefaultPageSize = 10;

    public const string SearchTooLong = "Use 100 characters or fewer.";

    public const string StatusInvalid = "Choose a valid status.";

    public const string PageInvalid = "Page must be 1 or more.";

    public const string PageSizeInvalid = "Page size must be 10, 25 or 50.";

    public static readonly int[] PageSizes = [10, 25, 50];

    /// <summary>The validated query, or null with the per-parameter errors.</summary>
    public static CustomerRecordQuery? Parse(
        CustomerRecordKind kind, CustomerRecordQueryText text, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var search = text.Search?.Trim();

        if (search is { Length: > SearchMaxLength })
        {
            errors["search"] = [SearchTooLong];
        }

        string? status = null;

        if (!string.IsNullOrEmpty(text.Status))
        {
            if (IsStatus(kind, text.Status))
            {
                status = text.Status;
            }
            else
            {
                errors["status"] = [StatusInvalid];
            }
        }

        var page = 1;

        if (!string.IsNullOrWhiteSpace(text.Page)
            && (!int.TryParse(text.Page.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors["page"] = [PageInvalid];
        }

        var pageSize = DefaultPageSize;

        if (!string.IsNullOrWhiteSpace(text.PageSize)
            && (!int.TryParse(text.PageSize.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
                || !PageSizes.Contains(pageSize)))
        {
            errors["pageSize"] = [PageSizeInvalid];
        }

        return errors.Count > 0
            ? null
            : new CustomerRecordQuery(string.IsNullOrEmpty(search) ? null : search, status, page, pageSize);
    }

    /// <summary>Status codes use the existing module maps: request status, quote display status, job operational status, invoice display status.</summary>
    private static bool IsStatus(CustomerRecordKind kind, string code) =>
        kind switch
        {
            CustomerRecordKind.Requests => RequestTransitions.TryParseCode(code, out _),
            CustomerRecordKind.Quotes => QuotesWorkspaceRules.TryParseStatus(code, QuotesTab.All, out var quote) && quote is not null,
            CustomerRecordKind.Jobs => JobsRules.TryParseStatus(code, JobsTab.All, out var job) && job is not null,
            _ => InvoiceStatusFilters.All.Contains(code, StringComparer.Ordinal),
        };
}
