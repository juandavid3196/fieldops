using System.Net;

namespace FieldOps.Domain.Quotes;

/// <summary>
/// The customer's answer to a sent version (customer-quote-approval BR-12 to BR-15). Totals exist only on an approval
/// (server-calculated, BR-11); the comment is the decline reason or the question.
/// </summary>
public sealed class QuoteResponse
{
    private static readonly QuoteStatus[] AllowedResponses =
    [
        QuoteStatus.Approved,
        QuoteStatus.Rejected,
        QuoteStatus.ClarificationRequested,
    ];

    private QuoteResponse()
    {
    }

    private QuoteResponse(
        Guid id,
        Guid organizationId,
        Guid quoteVersionId,
        QuoteStatus response,
        string responderName,
        DateTimeOffset respondedAt)
    {
        Id = id;
        OrganizationId = organizationId;
        QuoteVersionId = quoteVersionId;
        Response = response;
        ResponderName = responderName;
        RespondedAt = respondedAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid QuoteVersionId { get; private set; }

    public QuoteStatus Response { get; private set; }

    public string ResponderName { get; private set; } = string.Empty;

    public Guid? ResponderContactId { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset RespondedAt { get; private set; }

    public IPAddress? IpAddress { get; private set; }

    public decimal? Subtotal { get; private set; }

    public decimal? DiscountTotal { get; private set; }

    public decimal? TaxTotal { get; private set; }

    public decimal? Total { get; private set; }

    public static QuoteResponse Create(
        Guid organizationId,
        Guid quoteVersionId,
        QuoteStatus response,
        string responderName,
        DateTimeOffset? respondedAt = null,
        Guid? responderContactId = null,
        IPAddress? ipAddress = null,
        string? comment = null)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (quoteVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Quote version id is required.",
                nameof(quoteVersionId));
        }

        if (!AllowedResponses.Contains(response))
        {
            throw new ArgumentOutOfRangeException(
                nameof(response),
                response,
                "Response must be approved, rejected or clarification requested.");
        }

        if (string.IsNullOrWhiteSpace(responderName))
        {
            throw new ArgumentException(
                "Responder name is required.",
                nameof(responderName));
        }

        var now = respondedAt ?? DateTimeOffset.UtcNow;

        return new QuoteResponse(
            Guid.NewGuid(),
            organizationId,
            quoteVersionId,
            response,
            responderName.Trim(),
            new DateTimeOffset(now.UtcDateTime.Ticks - (now.UtcDateTime.Ticks % 10), TimeSpan.Zero))
        {
            ResponderContactId = responderContactId,
            IpAddress = ipAddress,
            Comment = comment,
        };
    }

    /// <summary>An approval: the server totals are required, as the schema check demands.</summary>
    public static QuoteResponse Approve(
        Guid organizationId,
        Guid quoteVersionId,
        string responderName,
        Guid? responderContactId,
        IPAddress? ipAddress,
        DateTimeOffset respondedAt,
        decimal subtotal,
        decimal discountTotal,
        decimal taxTotal,
        decimal total)
    {
        if (subtotal < 0 || discountTotal < 0 || taxTotal < 0 || total < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(total), total, "Totals cannot be negative.");
        }

        var approval = Create(
            organizationId, quoteVersionId, QuoteStatus.Approved, responderName, respondedAt, responderContactId, ipAddress);
        approval.Subtotal = subtotal;
        approval.DiscountTotal = discountTotal;
        approval.TaxTotal = taxTotal;
        approval.Total = total;

        return approval;
    }

    /// <summary>A decline with its reason; totals stay null.</summary>
    public static QuoteResponse Reject(
        Guid organizationId,
        Guid quoteVersionId,
        string responderName,
        Guid? responderContactId,
        IPAddress? ipAddress,
        DateTimeOffset respondedAt,
        string reason) =>
        Create(organizationId, quoteVersionId, QuoteStatus.Rejected, responderName, respondedAt, responderContactId, ipAddress, RequireText(reason));

    /// <summary>A question; totals stay null.</summary>
    public static QuoteResponse Ask(
        Guid organizationId,
        Guid quoteVersionId,
        string responderName,
        Guid? responderContactId,
        IPAddress? ipAddress,
        DateTimeOffset respondedAt,
        string message) =>
        Create(organizationId, quoteVersionId, QuoteStatus.ClarificationRequested, responderName, respondedAt, responderContactId, ipAddress, RequireText(message));

    private static string RequireText(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? throw new ArgumentException("The comment is required.", nameof(text))
            : text.Trim();
}
