namespace FieldOps.Domain.Quotes;

/// <summary>
/// An optional line of the approved version that the customer selected (customer-quote-approval BR-12). That the line
/// is optional and the response an approval is enforced by the application; the keys keep it in one organization and version.
/// </summary>
public sealed class QuoteResponseOptionalLine
{
    private QuoteResponseOptionalLine()
    {
    }

    public Guid OrganizationId { get; private set; }

    public Guid QuoteVersionId { get; private set; }

    public Guid QuoteResponseId { get; private set; }

    public Guid QuoteLineId { get; private set; }

    public static QuoteResponseOptionalLine Create(
        Guid organizationId,
        Guid quoteVersionId,
        Guid quoteResponseId,
        Guid quoteLineId)
    {
        if (organizationId == Guid.Empty || quoteVersionId == Guid.Empty || quoteResponseId == Guid.Empty || quoteLineId == Guid.Empty)
        {
            throw new ArgumentException("Organization, version, response and line ids are required.");
        }

        return new QuoteResponseOptionalLine
        {
            OrganizationId = organizationId,
            QuoteVersionId = quoteVersionId,
            QuoteResponseId = quoteResponseId,
            QuoteLineId = quoteLineId,
        };
    }
}
