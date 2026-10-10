using System.Globalization;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;

namespace FieldOps.Application.Features.InvoicePayments;

public enum AgingBucket
{
    Current,
    Days1To30,
    Days31To60,
    Days60Plus,
}

/// <summary>Display status, aging and average time to pay (invoices-payments-management BR-03 to BR-05), all in organization-local dates.</summary>
public static class InvoiceHubRules
{
    public static string StoredStatus(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => InvoiceStatusFilters.Draft,
        InvoiceStatus.Sent => InvoiceStatusFilters.Sent,
        InvoiceStatus.PartiallyPaid => InvoiceStatusFilters.PartiallyPaid,
        InvoiceStatus.Paid => InvoiceStatusFilters.Paid,
        InvoiceStatus.Overdue => InvoiceStatusFilters.Overdue,
        _ => "void",
    };

    /// <summary>A sent or partially paid invoice past its due date is overdue (BR-03); nothing is stored.</summary>
    public static bool IsOutstanding(InvoiceStatus status) => status is InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid;

    public static (string Status, int? DaysOverdue) Display(InvoiceStatus stored, DateOnly? dueDate, DateOnly today)
    {
        if (IsOutstanding(stored) && dueDate is { } due && due < today)
        {
            return (InvoiceStatusFilters.Overdue, today.DayNumber - due.DayNumber);
        }

        return (StoredStatus(stored), null);
    }

    public static string StatusLabel(string status, int? daysOverdue) => status switch
    {
        InvoiceStatusFilters.Draft => "Draft",
        InvoiceStatusFilters.Sent => "Sent",
        InvoiceStatusFilters.PartiallyPaid => "Partially paid",
        InvoiceStatusFilters.Paid => "Paid",
        InvoiceStatusFilters.Overdue => string.Create(
            CultureInfo.InvariantCulture, $"Overdue {daysOverdue ?? 0} {(daysOverdue == 1 ? "day" : "days")}"),
        _ => status,
    };

    /// <summary>Buckets by days past due (BR-05): not past due, 1 to 30, 31 to 60, 61 or more.</summary>
    public static AgingBucket Bucket(DateOnly? dueDate, DateOnly today)
    {
        var days = dueDate is { } due ? today.DayNumber - due.DayNumber : 0;

        return days switch
        {
            <= 0 => AgingBucket.Current,
            <= 30 => AgingBucket.Days1To30,
            <= 60 => AgingBucket.Days31To60,
            _ => AgingBucket.Days60Plus,
        };
    }

    /// <summary>
    /// Mean of (local date of the latest payment - issue date) over the invoices whose latest payment local date lies in
    /// today-89 to today (BR-04), rounded to one decimal; null when none.
    /// </summary>
    public static decimal? AverageDaysToPay(
        IEnumerable<(DateOnly IssueDate, DateTimeOffset LatestPaidAt)> invoices, TimeZoneInfo zone, DateOnly today)
    {
        var windowStart = today.AddDays(-(InvoicePaymentMessages.AverageWindowDays - 1));
        var days = invoices
            .Select(invoice => (invoice.IssueDate, Paid: OrganizationTime.LocalDate(invoice.LatestPaidAt, zone)))
            .Where(invoice => invoice.Paid >= windowStart && invoice.Paid <= today)
            .Select(invoice => invoice.Paid.DayNumber - invoice.IssueDate.DayNumber)
            .ToList();

        return days.Count == 0
            ? null
            : Math.Round((decimal)days.Sum(value => (long)value) / days.Count, 1, MidpointRounding.AwayFromZero) + 0.0m;
    }

    /// <summary>
    /// The first valid instant of a local date (OD-02): 00:00, or the first valid time after a DST gap that swallows the
    /// midnight, so the instant always converts back to the same local date.
    /// </summary>
    public static DateTimeOffset DayStartUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        for (var step = 0; step < 48 && zone.IsInvalidTime(local); step++)
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    public static string DisplayNumber(string prefix, long number) => RequestCardRules.DisplayNumber(prefix, number);

    public static bool TryParseNumber(string term, string prefix, out long number) => RequestSearch.TryParseNumber(term, prefix, out number);
}

/// <summary>Parses the hub queries (BR-07, BR-09, BR-19); every invalid value is a field error.</summary>
public static class InvoiceHubQueryParser
{
    public static BillingOutcome<InvoiceQuery> ParseInvoices(InvoiceQueryText text, bool paged)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var search = Search(text.Search, errors);
        var (from, to) = Range(text.From, text.To, errors);
        var branch = Id(text.BranchId, "branchId", InvoicePaymentMessages.BranchInvalid, errors);
        var customer = Id(text.CustomerId, "customerId", InvoicePaymentMessages.CustomerInvalid, errors);
        string? status = null;

        if (!string.IsNullOrWhiteSpace(text.Status))
        {
            var normalized = text.Status.Trim().ToLowerInvariant();

            if (InvoiceStatusFilters.All.Contains(normalized))
            {
                status = normalized;
            }
            else
            {
                errors["status"] = [InvoicePaymentMessages.StatusInvalid];
            }
        }

        var page = Page(text.Page, paged, errors);

        return errors.Count > 0
            ? new BillingOutcome<InvoiceQuery>.Invalid(errors)
            : new BillingOutcome<InvoiceQuery>.Succeeded(new InvoiceQuery(search, from, to, status, customer, branch, page));
    }

    public static BillingOutcome<PaymentQuery> ParsePayments(PaymentQueryText text, bool paged)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var search = Search(text.Search, errors);
        var (from, to) = Range(text.From, text.To, errors);
        var branch = Id(text.BranchId, "branchId", InvoicePaymentMessages.BranchInvalid, errors);
        PaymentMethod? method = null;

        if (!string.IsNullOrWhiteSpace(text.Method))
        {
            if (PaymentMethodCodes.TryParse(text.Method, out var parsed))
            {
                method = parsed;
            }
            else
            {
                errors["method"] = [InvoicePaymentMessages.MethodFilterInvalid];
            }
        }

        var page = Page(text.Page, paged, errors);

        return errors.Count > 0
            ? new BillingOutcome<PaymentQuery>.Invalid(errors)
            : new BillingOutcome<PaymentQuery>.Succeeded(new PaymentQuery(search, from, to, method, branch, page));
    }

    /// <summary>The overview and customers queries: an optional branch, or a search of 0 to 100 characters.</summary>
    public static BillingOutcome<Guid?> ParseBranch(string? branchId)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var branch = Id(branchId, "branchId", InvoicePaymentMessages.BranchInvalid, errors);

        return errors.Count > 0 ? new BillingOutcome<Guid?>.Invalid(errors) : new BillingOutcome<Guid?>.Succeeded(branch);
    }

    public static BillingOutcome<string?> ParseCustomerSearch(string? search)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var parsed = Search(search, errors);

        return errors.Count > 0 ? new BillingOutcome<string?>.Invalid(errors) : new BillingOutcome<string?>.Succeeded(parsed);
    }

    private static string? Search(string? value, Dictionary<string, string[]> errors)
    {
        var search = value?.Trim();

        if (search is { Length: > InvoicePaymentMessages.MaxSearchLength })
        {
            errors["search"] = [InvoicePaymentMessages.SearchTooLong];

            return null;
        }

        return string.IsNullOrEmpty(search) ? null : search;
    }

    private static (DateOnly?, DateOnly?) Range(string? fromText, string? toText, Dictionary<string, string[]> errors)
    {
        var from = Date(fromText, "from", errors);
        var to = Date(toText, "to", errors);

        if (from is { } start && to is { } end && start > end)
        {
            errors["to"] = [InvoicePaymentMessages.RangeInvalid];
        }

        return (from, to);
    }

    private static DateOnly? Date(string? text, string key, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors[key] = [InvoicePaymentMessages.DateInvalid];

        return null;
    }

    private static Guid? Id(string? text, string key, string message, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (Guid.TryParse(text.Trim(), out var id) && id != Guid.Empty)
        {
            return id;
        }

        errors[key] = [message];

        return null;
    }

    private static int Page(string? text, bool paged, Dictionary<string, string[]> errors)
    {
        var page = 1;

        if (paged
            && !string.IsNullOrWhiteSpace(text)
            && (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors["page"] = [InvoicePaymentMessages.PageInvalid];
        }

        return page;
    }
}

/// <summary>The payment field rules (BR-11, BR-16 format): every invalid value is a field error and nothing is written.</summary>
public static class PaymentFieldRules
{
    public static BillingOutcome<PaymentInput> Validate(PaymentBodyText body, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!Guid.TryParse(body.IdempotencyKey?.Trim(), out var key) || key == Guid.Empty)
        {
            errors["idempotencyKey"] = [InvoicePaymentMessages.KeyInvalid];
        }

        var amount = body.Amount ?? 0m;

        if (amount < 0.01m)
        {
            errors["amount"] = [InvoicePaymentMessages.AmountInvalid];
        }
        else if (decimal.Round(amount, 2) != amount)
        {
            errors["amount"] = [InvoicePaymentMessages.AmountPrecision];
        }

        DateOnly paidDate = default;

        if (!DateOnly.TryParseExact(body.PaidDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out paidDate))
        {
            errors["paidDate"] = [InvoicePaymentMessages.PaidDateInvalid];
        }
        else if (paidDate > today)
        {
            errors["paidDate"] = [InvoicePaymentMessages.PaidDateFuture];
        }

        // An online card payment is created only by the provider webhook (customer-invoice-payments BR-31 c).
        var methodValid = PaymentMethodCodes.TryParse(body.Method, out var method) && method != PaymentMethod.CardOnline;

        if (!methodValid)
        {
            errors["method"] = [InvoicePaymentMessages.MethodRequired];
        }

        var reference = body.Reference?.Trim();

        if (reference is { Length: > InvoicePaymentMessages.MaxReferenceLength })
        {
            errors["reference"] = [InvoicePaymentMessages.ReferenceTooLong];
        }
        else if (string.IsNullOrEmpty(reference) && methodValid && PaymentMethodCodes.RequiringReference.Contains(method))
        {
            errors["reference"] = [InvoicePaymentMessages.ReferenceRequired];
        }

        if (!Guid.TryParse(body.ReceivedByUserId?.Trim(), out var receivedBy) || receivedBy == Guid.Empty)
        {
            errors["receivedByUserId"] = [InvoicePaymentMessages.ReceivedByInvalid];
        }

        var note = body.Note?.Trim();

        if (note is { Length: > InvoicePaymentMessages.MaxNoteLength })
        {
            errors["note"] = [InvoicePaymentMessages.NoteTooLong];
        }

        return errors.Count > 0
            ? new BillingOutcome<PaymentInput>.Invalid(errors)
            : new BillingOutcome<PaymentInput>.Succeeded(new PaymentInput(
                key,
                amount,
                paidDate,
                method,
                string.IsNullOrEmpty(reference) ? null : reference,
                receivedBy,
                body.SendReceipt ?? false,
                string.IsNullOrEmpty(note) ? null : note,
                body.UpdatedAt));
    }
}
